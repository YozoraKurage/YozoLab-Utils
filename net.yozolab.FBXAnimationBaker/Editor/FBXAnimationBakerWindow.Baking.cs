using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// FBXAnimationBakerWindow のベイクパイプライン担当。
    ///
    /// 流れは 1 クリップにつき次の通り:
    ///   1. Source FBX をシーンへ一時インスタンス化し、Animator/Avatar を整える
    ///   2. AnimationMode で Humanoid クリップを 1 フレームずつサンプリングし、
    ///      全 Transform のローカル TRS(必要ならブレンドシェイプ)を記録する
    ///   3. 記録値から Generic(Transform) の AnimationClip を組み立てる
    ///   4. legacy コピーを Animation コンポーネントに載せ、Unity FBX Exporter で FBX を書き出す
    ///   5. 生成 FBX を再インポートし、Animation Type を設定する
    /// </summary>
    public partial class FBXAnimationBakerWindow
    {
        private const string LogPrefix = "[FBX Animation Baker]";

        /// <summary>
        /// ベイク結果が変わる修正を入れたら上げる版数。差分キャッシュの署名に含めており、
        /// パッケージ更新後は設定を触っていなくても Execute で作り直される。
        /// </summary>
        private const string BakerVersion = "5";

        /// <summary>1 チャンネルが「変化なし」とみなされる振れ幅のしきい値。</summary>
        private const float ConstantEpsilon = 1e-5f;

        /// <param name="ignoreCache">true のとき差分キャッシュを無視し、全エントリを強制的に再ベイクする。</param>
        private void ProcessBakeEntries(bool ignoreCache = false)
        {
            if (!FbxExporterBridge.IsAvailable)
            {
                Debug.LogError($"{LogPrefix} Unity FBX Exporter (com.unity.formats.fbx) is not installed. Install it from Package Manager first.");
                return;
            }

            // Animation ウィンドウのレコーディング中などに割り込むと、そちらの記録状態を壊してしまう
            if (AnimationMode.InAnimationMode())
            {
                Debug.LogError($"{LogPrefix} The editor is already in Animation Mode (Animation window recording?). Exit it before baking.");
                return;
            }

            if (settings.bakeEntries == null || settings.bakeEntries.Count == 0)
            {
                Debug.LogWarning($"{LogPrefix} No bake entries are registered.");
                return;
            }

            string defaultOutputPath = settings.outputDirectory != null
                ? AssetDatabase.GetAssetPath(settings.outputDirectory)
                : string.Empty;

            // 実際に処理する (エントリ, クリップ) の組を先に展開しておくと進捗表示が素直になる
            var jobs = new List<(AnimationBakeEntry entry, AnimationClip clip, bool multiClip)>();
            int disabledCount = 0;
            foreach (AnimationBakeEntry entry in settings.bakeEntries)
            {
                if (entry == null)
                {
                    continue;
                }
                if (!entry.enabled)
                {
                    disabledCount++;
                    continue;
                }
                if (entry.sourceFbx == null)
                {
                    Debug.LogWarning($"{LogPrefix} Source FBX is not set, skipped: {GetEntryDisplayName(entry)}");
                    continue;
                }

                List<AnimationClip> clips = entry.clips?.Where(c => c != null).ToList() ?? new List<AnimationClip>();
                if (clips.Count == 0)
                {
                    Debug.LogWarning($"{LogPrefix} No animation clip is set, skipped: {GetEntryDisplayName(entry)}");
                    continue;
                }

                foreach (AnimationClip clip in clips)
                {
                    jobs.Add((entry, clip, clips.Count > 1));
                }
            }

            if (jobs.Count == 0)
            {
                Debug.LogWarning($"{LogPrefix} Nothing to bake (enabled entries: {settings.bakeEntries.Count - disabledCount}).");
                return;
            }

            Debug.Log($"{LogPrefix} Baking started: {jobs.Count} clip(s){(ignoreCache ? " (cache ignored: full re-bake)" : string.Empty)}");
            int bakedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            var generatedPaths = new List<(string path, AnimationBakeEntry entry)>();

            // 1 本ごとにインポートを走らせると待ち時間が積み上がるため、
            // 生成中はインポートを止めておき、最後にまとめて 1 回で処理させる。
            AssetDatabase.StartAssetEditing();
            bool assetEditingStarted = true;

            try
            {
                for (int i = 0; i < jobs.Count; i++)
                {
                    (AnimationBakeEntry entry, AnimationClip clip, bool multiClip) = jobs[i];

                    string outputFolder = GetEntryOutputFolder(entry, defaultOutputPath);
                    if (string.IsNullOrEmpty(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder))
                    {
                        Debug.LogWarning($"{LogPrefix} Output folder is invalid, skipped: {GetEntryDisplayName(entry)} / {clip.name}");
                        failedCount++;
                        continue;
                    }

                    string outputName = GetOutputName(entry, clip, multiClip);
                    string outputAssetPath = $"{outputFolder}/{outputName}.fbx";

                    EditorUtility.DisplayProgressBar("FBX Animation Baker",
                        $"Baking: {outputName} ({i + 1}/{jobs.Count})",
                        (float)i / jobs.Count);

                    // out 値(ハッシュ/署名)はキャッシュ更新に必要なため常に計算し、スキップ判定だけ ignoreCache で抑止する
                    bool canSkip = ShouldSkipBake(entry, clip, outputAssetPath,
                        out string sourceHash, out string clipHash, out string entrySignature);
                    if (!ignoreCache && canSkip)
                    {
                        skippedCount++;
                        Debug.Log($"{LogPrefix} No changes, skipped: {outputName}");
                        continue;
                    }

                    if (BakeSingleClip(entry, clip, outputFolder, outputName, outputAssetPath))
                    {
                        UpdateBakeCache(outputAssetPath, sourceHash, clipHash, entrySignature);
                        generatedPaths.Add((outputAssetPath, entry));
                        bakedCount++;
                        Debug.Log($"{LogPrefix} Baked: {outputAssetPath}");
                    }
                    else
                    {
                        failedCount++;
                    }
                }

                EditorUtility.DisplayProgressBar("FBX Animation Baker", "Importing generated FBX...", 1f);
                AssetDatabase.StopAssetEditing();
                assetEditingStarted = false;

                ApplyImportSettings(generatedPaths);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                settings.SaveSettings();

                Debug.Log($"{LogPrefix} All done: total={jobs.Count}, baked={bakedCount}, skipped={skippedCount}, failed={failedCount}, entry-off={disabledCount}");
            }
            finally
            {
                if (assetEditingStarted)
                {
                    AssetDatabase.StopAssetEditing();
                }
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>1 クリップ分のベイクと FBX 書き出し。成功したら true。</summary>
        private bool BakeSingleClip(AnimationBakeEntry entry, AnimationClip clip, string outputFolder, string outputName, string outputAssetPath)
        {
            GameObject instance = null;
            AnimationClip bakedClip = null;
            AnimationClip legacyClip = null;
            List<Mesh> temporaryMeshes = null;
            bool animationModeStarted = false;

            try
            {
                // PrefabUtility.InstantiatePrefab だと Prefab インスタンスになり、
                // 後段の AddComponent/DestroyImmediate が「Prefab の構造変更」として弾かれる。
                // 一時オブジェクトなので、素の Instantiate で接続なしの複製を作る。
                instance = UnityEngine.Object.Instantiate(entry.sourceFbx);
                if (instance == null)
                {
                    Debug.LogWarning($"{LogPrefix} Failed to instantiate source FBX: {AssetDatabase.GetAssetPath(entry.sourceFbx)}");
                    return false;
                }

                instance.name = outputName;
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                Animator animator = instance.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = instance.AddComponent<Animator>();
                }
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = entry.bakeRootMotion;

                if (entry.useOtherAvatarDefinition)
                {
                    if (entry.avatarDefinition != null)
                    {
                        animator.avatar = entry.avatarDefinition;
                    }
                    else
                    {
                        Debug.LogWarning($"{LogPrefix} 'Use other avatar definition' is enabled but Avatar is not set. Falling back to the FBX avatar: {GetEntryDisplayName(entry)}");
                    }
                }

                if (clip.isHumanMotion && (animator.avatar == null || !animator.avatar.isHuman))
                {
                    Debug.LogWarning($"{LogPrefix} \"{clip.name}\" is a humanoid clip but the model has no humanoid Avatar. The result may be empty: {GetEntryDisplayName(entry)}");
                }

                // ── サンプリング ──────────────────────────────────────────
                var samples = new BakeSampleBuffer(instance, entry.bakeBlendShapes);
                float fps = ResolveFrameRate(entry, clip);
                float dt = 1f / fps;

                // 1 フレームだけのポーズクリップは length が 0 になる。そのまま同じ時刻に
                // 2 キー打つと壊れたカーブになるので、最低 1 フレーム分の長さを確保する。
                float duration = Mathf.Max(clip.length, dt);
                int frameCount = Mathf.Max(2, Mathf.RoundToInt(duration * fps) + 1);

                AnimationMode.StartAnimationMode();
                animationModeStarted = true;

                for (int frame = 0; frame < frameCount; frame++)
                {
                    float time = Mathf.Min(frame * dt, duration);

                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(instance, clip, time);
                    AnimationMode.EndSampling();

                    samples.Capture(time);
                }

                AnimationMode.StopAnimationMode();
                animationModeStarted = false;

                // ── カーブ生成 ────────────────────────────────────────────
                bakedClip = samples.BuildClip(entry, fps, entry.removeConstantCurves, out int curveCount);

                // 1 フレームのポーズクリップは全チャンネルが「変化なし」になるため、
                // 定数カーブ除去をそのまま適用するとカーブが 1 本も残らず、
                // アニメーションの入っていない FBX ができてしまう。その場合は除去せず作り直す。
                if (curveCount == 0 && entry.removeConstantCurves)
                {
                    UnityEngine.Object.DestroyImmediate(bakedClip);
                    bakedClip = samples.BuildClip(entry, fps, false, out curveCount);
                    Debug.Log($"{LogPrefix} \"{clip.name}\" has no changing curve (static pose). Baked it with Remove Constant Curves disabled.");
                }

                if (curveCount == 0)
                {
                    Debug.LogWarning($"{LogPrefix} No curve was baked for \"{clip.name}\". The generated FBX will have no animation.");
                }

                bakedClip.name = outputName;

                AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(bakedClip);
                AnimationClipSettings sourceClipSettings = AnimationUtility.GetAnimationClipSettings(clip);
                clipSettings.startTime = 0f;
                clipSettings.stopTime = Mathf.Max(dt, samples.LastTime);
                clipSettings.loopTime = sourceClipSettings.loopTime;
                AnimationUtility.SetAnimationClipSettings(bakedClip, clipSettings);

                if (entry.saveBakedClipAsset)
                {
                    SaveBakedClipAsset(bakedClip, $"{outputFolder}/{outputName}.anim");
                }

                // ── エクスポート用ヒエラルキーの用意 ─────────────────────
                // Animator が残っていると Exporter 側がどちらのアニメーションを見るか曖昧になるため、
                // legacy の Animation コンポーネント 1 本に寄せる。
                UnityEngine.Object.DestroyImmediate(animator);

                samples.ApplyFirstFrame();

                if (entry.exportContent == BakeExportContent.SkeletonOnly)
                {
                    StripRenderers(instance, entry.bakeBlendShapes);
                }

                if (entry.excludeBlendShapes)
                {
                    if (entry.bakeBlendShapes)
                    {
                        Debug.LogWarning($"{LogPrefix} 'Exclude BlendShapes' is ignored because 'Bake BlendShapes' is enabled: {GetEntryDisplayName(entry)}");
                    }
                    else
                    {
                        temporaryMeshes = StripBlendShapes(instance);
                    }
                }

                legacyClip = new AnimationClip { name = outputName };
                EditorUtility.CopySerialized(bakedClip, legacyClip);
                legacyClip.name = outputName;
                legacyClip.legacy = true;

                Animation animation = instance.GetComponent<Animation>();
                if (animation == null)
                {
                    animation = instance.AddComponent<Animation>();
                }
                animation.AddClip(legacyClip, legacyClip.name);
                animation.clip = legacyClip;

                // ── FBX 書き出し ─────────────────────────────────────────
                string absolutePath = ToAbsolutePath(outputAssetPath);
                if (!FbxExporterBridge.Export(absolutePath, instance, entry.exportAscii, entry.bakeBlendShapes, out string exportError))
                {
                    Debug.LogError($"{LogPrefix} FBX export failed for \"{outputName}\": {exportError}");
                    return false;
                }

                // 実際のインポートは StartAssetEditing 中なので、ここでは予約だけされる。
                // インポート設定は全件書き出したあとに ApplyImportSettings でまとめて当てる。
                AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);

                long fileSizeKb = new FileInfo(absolutePath).Length / 1024;
                Debug.Log($"{LogPrefix} \"{outputName}\": {curveCount} curve(s), {CountKeys(bakedClip)} key(s), {frameCount} sampled frame(s), {fileSizeKb} KB");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} Exception while baking \"{outputName}\": {e}");
                return false;
            }
            finally
            {
                if (animationModeStarted)
                {
                    AnimationMode.StopAnimationMode();
                }
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
                if (bakedClip != null)
                {
                    UnityEngine.Object.DestroyImmediate(bakedClip);
                }
                if (legacyClip != null)
                {
                    UnityEngine.Object.DestroyImmediate(legacyClip);
                }
                if (temporaryMeshes != null)
                {
                    foreach (Mesh mesh in temporaryMeshes)
                    {
                        UnityEngine.Object.DestroyImmediate(mesh);
                    }
                }
            }
        }

        /// <summary>
        /// エクスポート対象のメッシュからブレンドシェイプを取り除く。
        ///
        /// ブレンドシェイプはメッシュ側のデータなので、カーブをベイクしなくても
        /// FBX には常に含まれてしまう(そして容量の大半を占める)。
        /// ここでブレンドシェイプ抜きのメッシュを作って差し替える。
        /// 返した一時メッシュは呼び出し側で破棄すること。
        /// </summary>
        private static List<Mesh> StripBlendShapes(GameObject instance)
        {
            var temporaryMeshes = new List<Mesh>();

            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0)
                {
                    continue;
                }

                Mesh stripped = CreateMeshWithoutBlendShapes(mesh);
                renderer.sharedMesh = stripped;
                temporaryMeshes.Add(stripped);
            }

            return temporaryMeshes;
        }

        /// <summary>ブレンドシェイプ以外のメッシュデータを複製した新しいメッシュを返す。</summary>
        private static Mesh CreateMeshWithoutBlendShapes(Mesh source)
        {
            var copy = new Mesh
            {
                name = source.name,
                indexFormat = source.indexFormat,
            };

            copy.vertices = source.vertices;
            copy.normals = source.normals;
            copy.tangents = source.tangents;
            copy.colors32 = source.colors32;
            copy.uv = source.uv;
            copy.uv2 = source.uv2;
            copy.uv3 = source.uv3;
            copy.uv4 = source.uv4;
            copy.uv5 = source.uv5;
            copy.uv6 = source.uv6;
            copy.uv7 = source.uv7;
            copy.uv8 = source.uv8;
            copy.bindposes = source.bindposes;

            // 4 影響を超えるスキニングを落とさないよう、可能なら可変長のボーンウェイトを使う
            var bonesPerVertex = source.GetBonesPerVertex();
            if (bonesPerVertex.Length > 0)
            {
                copy.SetBoneWeights(bonesPerVertex, source.GetAllBoneWeights());
            }
            else
            {
                copy.boneWeights = source.boneWeights;
            }

            copy.subMeshCount = source.subMeshCount;
            for (int i = 0; i < source.subMeshCount; i++)
            {
                copy.SetIndices(source.GetIndices(i), source.GetTopology(i), i, false);
            }

            copy.RecalculateBounds();
            return copy;
        }

        /// <summary>クリップ内の全キー数。ベイク結果のサイズ感をログに出すために使う。</summary>
        private static int CountKeys(AnimationClip clip)
        {
            int count = 0;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                count += curve != null ? curve.length : 0;
            }
            return count;
        }

        /// <summary>
        /// メッシュ/レンダラーを取り除き、アニメーションするノード階層だけを残す。
        /// メッシュとブレンドシェイプが FBX 容量の大半を占めるため、
        /// アニメーションだけが欲しい場合はこれで劇的に小さくなる。
        /// </summary>
        private static void StripRenderers(GameObject instance, bool keepSkinnedMeshes)
        {
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                UnityEngine.Object.DestroyImmediate(filter);
            }

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                // ブレンドシェイプをベイクする場合、対象の SkinnedMeshRenderer を消すと
                // カーブの参照先が無くなるため残す(その分ファイルは大きくなる)。
                if (keepSkinnedMeshes && renderer is SkinnedMeshRenderer)
                {
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(renderer);
            }
        }

        /// <summary>ベイク済みクリップを .anim として保存する。既存ファイルがある場合は GUID を保持したまま中身を差し替える。</summary>
        private static void SaveBakedClipAsset(AnimationClip bakedClip, string clipAssetPath)
        {
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipAssetPath);
            if (existing != null)
            {
                existing.ClearCurves();
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(bakedClip))
                {
                    AnimationUtility.SetEditorCurve(existing, binding, AnimationUtility.GetEditorCurve(bakedClip, binding));
                }
                AnimationUtility.SetAnimationClipSettings(existing, AnimationUtility.GetAnimationClipSettings(bakedClip));
                existing.frameRate = bakedClip.frameRate;
                EditorUtility.SetDirty(existing);
                return;
            }

            AnimationClip newClip = new AnimationClip();
            EditorUtility.CopySerialized(bakedClip, newClip);
            newClip.name = Path.GetFileNameWithoutExtension(clipAssetPath);
            AssetDatabase.CreateAsset(newClip, clipAssetPath);
        }

        /// <summary>
        /// 生成した FBX のインポート設定をまとめて適用する。
        ///
        /// AssetPostprocessor を使えば初回インポートで設定を当てられるが、
        /// ポストプロセッサを持つアセンブリが変わるたびに Unity がプロジェクト内の
        /// 全モデルを再インポートしてしまうため、この方式は採らない。
        /// 代わりに、設定が実際に変わるものだけを 1 バッチで再インポートする
        /// (2 回目以降のベイクでは .meta に設定が残っているので再インポートは走らない)。
        /// </summary>
        private static void ApplyImportSettings(List<(string path, AnimationBakeEntry entry)> generatedPaths)
        {
            if (generatedPaths.Count == 0)
            {
                return;
            }

            var pendingReimport = new List<ModelImporter>();

            foreach ((string path, AnimationBakeEntry entry) in generatedPaths)
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    Debug.LogWarning($"{LogPrefix} Failed to get ModelImporter for the generated FBX: {path}");
                    continue;
                }

                if (ConfigureImporter(importer, entry))
                {
                    pendingReimport.Add(importer);
                }
            }

            if (pendingReimport.Count == 0)
            {
                return;
            }

            EditorUtility.DisplayProgressBar("FBX Animation Baker",
                $"Applying import settings ({pendingReimport.Count})...", 1f);

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (ModelImporter importer in pendingReimport)
                {
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        /// <summary>
        /// エントリの設定を ModelImporter へ反映する。実際に変更があったときだけ true を返す。
        /// 変更がなければ再インポートを走らせないため、再ベイク時のインポートは 1 回で済む。
        /// </summary>
        private static bool ConfigureImporter(ModelImporter importer, AnimationBakeEntry entry)
        {
            bool changed = false;

            if (entry.importAnimationType != BakedFbxAnimationType.None)
            {
                ModelImporterAnimationType animationType;
                switch (entry.importAnimationType)
                {
                    case BakedFbxAnimationType.Legacy:
                        animationType = ModelImporterAnimationType.Legacy;
                        break;
                    case BakedFbxAnimationType.Humanoid:
                        animationType = ModelImporterAnimationType.Human;
                        break;
                    default:
                        animationType = ModelImporterAnimationType.Generic;
                        break;
                }

                if (importer.animationType != animationType)
                {
                    importer.animationType = animationType;
                    changed = true;
                }

                if (animationType == ModelImporterAnimationType.Human
                    && importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    changed = true;
                }

                if (!importer.importAnimation)
                {
                    importer.importAnimation = true;
                    changed = true;
                }
            }

            if (!entry.fastImport)
            {
                return changed;
            }

            bool skeletonOnly = entry.exportContent == BakeExportContent.SkeletonOnly;
            // 書き出し側で外したものは読み込む必要がない
            bool importBlendShapes = !skeletonOnly && !entry.excludeBlendShapes;
            ModelImporterTangents tangents = skeletonOnly
                ? ModelImporterTangents.None
                : ModelImporterTangents.CalculateMikk;

            // 既にベイク済みのカーブなので、インポート時の再サンプリング/圧縮は
            // 時間がかかるだけで得がない(キーもずれる)。
            if (importer.resampleCurves)
            {
                importer.resampleCurves = false;
                changed = true;
            }
            if (importer.animationCompression != ModelImporterAnimationCompression.Off)
            {
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                changed = true;
            }
            if (importer.importBlendShapes != importBlendShapes)
            {
                importer.importBlendShapes = importBlendShapes;
                changed = true;
            }
            if (importer.importTangents != tangents)
            {
                importer.importTangents = tangents;
                changed = true;
            }

            // アニメーション用の FBX では使わないものを切る。
            // マテリアル生成はテクスチャ探索を伴うため、体感で一番効く。
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                changed = true;
            }
            if (importer.importCameras)
            {
                importer.importCameras = false;
                changed = true;
            }
            if (importer.importLights)
            {
                importer.importLights = false;
                changed = true;
            }
            if (importer.importVisibility)
            {
                importer.importVisibility = false;
                changed = true;
            }
            if (importer.importConstraints)
            {
                importer.importConstraints = false;
                changed = true;
            }
            if (importer.isReadable)
            {
                importer.isReadable = false;
                changed = true;
            }

            return changed;
        }

        private static float ResolveFrameRate(AnimationBakeEntry entry, AnimationClip clip)
        {
            if (entry.frameRate > 0f)
            {
                return entry.frameRate;
            }
            return clip.frameRate > 0f ? clip.frameRate : 30f;
        }

        // ═══════════════════════════════════════════════════════════════
        //  出力先 / 名前の解決
        // ═══════════════════════════════════════════════════════════════

        private string GetEntryOutputFolder(AnimationBakeEntry entry)
        {
            string defaultOutputPath = settings != null && settings.outputDirectory != null
                ? AssetDatabase.GetAssetPath(settings.outputDirectory)
                : string.Empty;
            return GetEntryOutputFolder(entry, defaultOutputPath);
        }

        private string GetEntryOutputFolder(AnimationBakeEntry entry, string defaultOutputPath)
        {
            if (entry != null && entry.outputDirectoryOverride != null)
            {
                string overridePath = AssetDatabase.GetAssetPath(entry.outputDirectoryOverride);
                if (!string.IsNullOrEmpty(overridePath) && AssetDatabase.IsValidFolder(overridePath))
                {
                    return overridePath;
                }

                Debug.LogWarning($"{LogPrefix} Entry '{GetEntryDisplayName(entry)}' has an invalid Output Directory override; using the global Output Directory instead.");
            }
            return defaultOutputPath;
        }

        /// <summary>
        /// 出力ファイル名(拡張子なし)を解決する。outputFileName 未設定ならクリップ名、
        /// 1 エントリに複数クリップがある場合は名前の衝突を避けるためクリップ名を後置する。
        /// </summary>
        private static string GetOutputName(AnimationBakeEntry entry, AnimationClip clip, bool multiClip)
        {
            string requested = SanitizeFileName(entry?.outputFileName);
            string clipName = SanitizeFileName(clip.name);
            if (string.IsNullOrEmpty(clipName))
            {
                clipName = "clip";
            }

            if (string.IsNullOrEmpty(requested))
            {
                return clipName;
            }

            return multiClip ? $"{requested}_{clipName}" : requested;
        }

        private static string SanitizeFileName(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                return string.Empty;
            }

            string name = requested.Trim();
            if (name.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".fbx".Length).TrimEnd();
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c.ToString(), string.Empty);
            }

            return name.Trim();
        }

        internal static string GetEntryDisplayName(AnimationBakeEntry entry)
        {
            if (entry == null)
            {
                return "(null)";
            }
            if (!string.IsNullOrWhiteSpace(entry.displayName))
            {
                return entry.displayName.Trim();
            }
            return entry.sourceFbx != null ? entry.sourceFbx.name : "(no FBX)";
        }

        /// <summary>"Assets/..." のアセットパスをプロジェクト外からも扱える絶対パスへ変換する。</summary>
        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        // ═══════════════════════════════════════════════════════════════
        //  差分キャッシュ
        // ═══════════════════════════════════════════════════════════════

        private bool ShouldSkipBake(AnimationBakeEntry entry, AnimationClip clip, string outputAssetPath,
            out string sourceHash, out string clipHash, out string entrySignature)
        {
            sourceHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(entry.sourceFbx)).ToString();
            clipHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(clip)).ToString();
            entrySignature = BuildEntrySignature(entry, clip);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(outputAssetPath) == null)
            {
                return false;
            }

            BakeCacheEntry cacheEntry = GetBakeCacheEntry(outputAssetPath);
            if (cacheEntry == null)
            {
                return false;
            }

            return string.Equals(cacheEntry.sourceDependencyHash, sourceHash, StringComparison.Ordinal)
                && string.Equals(cacheEntry.clipDependencyHash, clipHash, StringComparison.Ordinal)
                && string.Equals(cacheEntry.entrySignature, entrySignature, StringComparison.Ordinal);
        }

        private static string BuildEntrySignature(AnimationBakeEntry entry, AnimationClip clip)
        {
            var sb = new StringBuilder();

            // ベイク結果が変わる修正を入れたらこれを上げる。
            // 署名が変わることで、設定を触っていなくても Execute で作り直される。
            sb.Append(BakerVersion).Append('|');
            sb.Append(clip.name).Append('|');
            sb.Append(entry.frameRate).Append('|');
            sb.Append(entry.bakeRootMotion).Append('|');
            sb.Append(entry.bakeScale).Append('|');
            sb.Append(entry.bakeBlendShapes).Append('|');
            sb.Append(entry.excludeBlendShapes).Append('|');
            sb.Append(entry.removeConstantCurves).Append('|');
            sb.Append(entry.keyframeReduction).Append('|');
            sb.Append(entry.reductionTolerance).Append('|');
            sb.Append(entry.exportContent).Append('|');
            sb.Append(entry.saveBakedClipAsset).Append('|');
            sb.Append(entry.importAnimationType).Append('|');
            sb.Append(entry.fastImport).Append('|');
            sb.Append(entry.exportAscii).Append('|');
            sb.Append(entry.useOtherAvatarDefinition).Append('|');
            sb.Append(entry.avatarDefinition != null ? AssetDatabase.GetAssetPath(entry.avatarDefinition) : string.Empty);
            return sb.ToString();
        }

        private BakeCacheEntry GetBakeCacheEntry(string outputAssetPath)
        {
            if (settings.bakeCacheEntries == null)
            {
                settings.bakeCacheEntries = new List<BakeCacheEntry>();
            }

            return settings.bakeCacheEntries.FirstOrDefault(
                e => e != null && string.Equals(e.outputFbxAssetPath, outputAssetPath, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateBakeCache(string outputAssetPath, string sourceHash, string clipHash, string entrySignature)
        {
            BakeCacheEntry cacheEntry = GetBakeCacheEntry(outputAssetPath);
            if (cacheEntry == null)
            {
                cacheEntry = new BakeCacheEntry();
                settings.bakeCacheEntries.Add(cacheEntry);
            }

            cacheEntry.outputFbxAssetPath = outputAssetPath;
            cacheEntry.sourceDependencyHash = sourceHash;
            cacheEntry.clipDependencyHash = clipHash;
            cacheEntry.entrySignature = entrySignature;
            EditorUtility.SetDirty(settings);
        }

        // ═══════════════════════════════════════════════════════════════
        //  サンプリングバッファ
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// サンプリング中の Transform / ブレンドシェイプ値を貯め込み、最後に Generic クリップへ変換する。
        /// </summary>
        private class BakeSampleBuffer
        {
            private readonly Transform root;
            private readonly List<TransformTrack> transformTracks = new List<TransformTrack>();
            private readonly List<BlendShapeTrack> blendShapeTracks = new List<BlendShapeTrack>();
            private readonly List<float> times = new List<float>();

            public float LastTime => times.Count > 0 ? times[times.Count - 1] : 0f;

            public BakeSampleBuffer(GameObject instance, bool captureBlendShapes)
            {
                root = instance.transform;

                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    transformTracks.Add(new TransformTrack
                    {
                        target = t,
                        path = AnimationUtility.CalculateTransformPath(t, root),
                    });
                }

                if (!captureBlendShapes)
                {
                    return;
                }

                foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Mesh mesh = renderer.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        blendShapeTracks.Add(new BlendShapeTrack
                        {
                            renderer = renderer,
                            index = i,
                            path = AnimationUtility.CalculateTransformPath(renderer.transform, root),
                            propertyName = $"blendShape.{mesh.GetBlendShapeName(i)}",
                        });
                    }
                }
            }

            /// <summary>現在のシーン上の状態を 1 フレーム分記録する。</summary>
            public void Capture(float time)
            {
                times.Add(time);

                foreach (TransformTrack track in transformTracks)
                {
                    Quaternion rotation = track.target.localRotation;

                    // 四元数の連続性を保つ(前フレームと逆半球なら符号を反転)。
                    // これをしないと補間時に回転がひっくり返る。
                    if (track.rotations.Count > 0)
                    {
                        Quaternion previous = track.rotations[track.rotations.Count - 1];
                        if (Quaternion.Dot(previous, rotation) < 0f)
                        {
                            rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                        }
                    }

                    track.positions.Add(track.target.localPosition);
                    track.rotations.Add(rotation);
                    track.scales.Add(track.target.localScale);
                }

                foreach (BlendShapeTrack track in blendShapeTracks)
                {
                    track.weights.Add(track.renderer.GetBlendShapeWeight(track.index));
                }
            }

            /// <summary>
            /// 記録した先頭フレームの姿勢をシーン上のインスタンスへ書き戻す。
            /// 定数カーブを削除した Transform は FBX 側でこの姿勢のまま固定されるため、
            /// エクスポート直前に 0 フレーム目へ戻しておく必要がある。
            /// </summary>
            public void ApplyFirstFrame()
            {
                foreach (TransformTrack track in transformTracks)
                {
                    if (track.target == null || track.positions.Count == 0)
                    {
                        continue;
                    }

                    track.target.localPosition = track.positions[0];
                    track.target.localRotation = track.rotations[0];
                    track.target.localScale = track.scales[0];
                }

                foreach (BlendShapeTrack track in blendShapeTracks)
                {
                    if (track.renderer == null || track.weights.Count == 0)
                    {
                        continue;
                    }
                    track.renderer.SetBlendShapeWeight(track.index, track.weights[0]);
                }
            }

            /// <param name="removeConstant">
            /// 変化しないカーブを省くか。ポーズクリップのように全カーブが定数のときは、
            /// 呼び出し側が false で作り直す。
            /// </param>
            /// <param name="curveCount">実際に書き込まれたカーブ本数。0 ならアニメーションなし。</param>
            public AnimationClip BuildClip(AnimationBakeEntry entry, float frameRate, bool removeConstant, out int curveCount)
            {
                var clip = new AnimationClip { frameRate = frameRate };
                float[] timeArray = times.ToArray();
                bool reduce = entry.keyframeReduction;
                float tolerance = Mathf.Max(0f, entry.reductionTolerance);
                curveCount = 0;

                foreach (TransformTrack track in transformTracks)
                {
                    curveCount += SetCurveGroup(clip, track.path, timeArray, removeConstant, reduce, tolerance,
                        ("m_LocalPosition.x", track.positions.Select(p => p.x).ToArray()),
                        ("m_LocalPosition.y", track.positions.Select(p => p.y).ToArray()),
                        ("m_LocalPosition.z", track.positions.Select(p => p.z).ToArray()));

                    curveCount += SetCurveGroup(clip, track.path, timeArray, removeConstant, reduce, tolerance,
                        ("m_LocalRotation.x", track.rotations.Select(r => r.x).ToArray()),
                        ("m_LocalRotation.y", track.rotations.Select(r => r.y).ToArray()),
                        ("m_LocalRotation.z", track.rotations.Select(r => r.z).ToArray()),
                        ("m_LocalRotation.w", track.rotations.Select(r => r.w).ToArray()));

                    if (entry.bakeScale)
                    {
                        curveCount += SetCurveGroup(clip, track.path, timeArray, removeConstant, reduce, tolerance,
                            ("m_LocalScale.x", track.scales.Select(s => s.x).ToArray()),
                            ("m_LocalScale.y", track.scales.Select(s => s.y).ToArray()),
                            ("m_LocalScale.z", track.scales.Select(s => s.z).ToArray()));
                    }
                }

                foreach (BlendShapeTrack track in blendShapeTracks)
                {
                    float[] values = track.weights.ToArray();
                    if (removeConstant && IsConstant(values))
                    {
                        continue;
                    }

                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(track.path, typeof(SkinnedMeshRenderer), track.propertyName),
                        BuildLinearCurve(timeArray, values, reduce, tolerance));
                    curveCount++;
                }

                return clip;
            }

            /// <summary>
            /// 1 つのプロパティ(位置/回転/スケール)を構成するチャンネルをまとめて設定する。
            /// 「全チャンネルが変化なし」のときだけ、まとめて省略する。書き込んだカーブ本数を返す。
            /// </summary>
            private static int SetCurveGroup(AnimationClip clip, string path, float[] times,
                bool removeConstant, bool reduce, float tolerance,
                params (string property, float[] values)[] channels)
            {
                if (removeConstant && channels.All(c => IsConstant(c.values)))
                {
                    return 0;
                }

                foreach ((string property, float[] values) in channels)
                {
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(path, typeof(Transform), property),
                        BuildLinearCurve(times, values, reduce, tolerance));
                }
                return channels.Length;
            }

            private static bool IsConstant(float[] values)
            {
                if (values.Length == 0)
                {
                    return true;
                }

                float min = values[0];
                float max = values[0];
                for (int i = 1; i < values.Length; i++)
                {
                    min = Mathf.Min(min, values[i]);
                    max = Mathf.Max(max, values[i]);
                }
                return max - min <= ConstantEpsilon;
            }

            /// <summary>
            /// 毎フレーム値を持つベイク済みカーブなので、補間は線形にしておく。
            /// Auto のままだとキー間でオーバーシュートし、元のモーションとずれる。
            /// </summary>
            private static AnimationCurve BuildLinearCurve(float[] times, float[] values, bool reduce, float tolerance)
            {
                var keys = new List<Keyframe>(times.Length);
                for (int i = 0; i < times.Length; i++)
                {
                    // 直前に残したキーと次のサンプルを結んだ直線上に乗るキーは落とす。
                    // 毎フレームキーのままだと FBX が肥大化するため。
                    if (reduce && i > 0 && i < times.Length - 1 && keys.Count > 0)
                    {
                        Keyframe last = keys[keys.Count - 1];
                        float span = times[i + 1] - last.time;
                        float expected = span > Mathf.Epsilon
                            ? Mathf.Lerp(last.value, values[i + 1], (times[i] - last.time) / span)
                            : last.value;

                        if (Mathf.Abs(values[i] - expected) <= tolerance)
                        {
                            continue;
                        }
                    }

                    keys.Add(new Keyframe(times[i], values[i]));
                }

                var curve = new AnimationCurve(keys.ToArray());
                for (int i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                return curve;
            }

            private class TransformTrack
            {
                public Transform target;
                public string path;
                public readonly List<Vector3> positions = new List<Vector3>();
                public readonly List<Quaternion> rotations = new List<Quaternion>();
                public readonly List<Vector3> scales = new List<Vector3>();
            }

            private class BlendShapeTrack
            {
                public SkinnedMeshRenderer renderer;
                public int index;
                public string path;
                public string propertyName;
                public readonly List<float> weights = new List<float>();
            }
        }
    }
}
