using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// 再生中のアバターの「現在の確定状態」（PhysBoneなどの揺れ物が落ち着いたボーンTransform、
    /// ブレンドシェイプ値、レンダラーの有効/GameObjectのアクティブ状態）を、
    /// 自己完結した1つのPrefabアセットとして固定保存するコア処理。
    ///
    /// 静的メッシュ化（StaticMeshBaker）の前段に当たり、揺れ物の結果を含んだポーズを
    /// 永続化することで、他ユーザーにポーズ編集を許さない配布物向けの素材を作る。
    ///
    /// 再生中のシーン変更は再生終了時に破棄されるため、キャプチャと保存を再生中の1操作で完結させる
    /// （PoseBakerのようなドメインリロードを跨ぐLibrary退避は不要）。
    ///
    /// NDMF(Apply on Play)環境では、ビルド後の再生中アバターをそのままクローンするため、
    /// ビルドで動的生成されたメッシュ/マテリアル/テクスチャはサブアセットとして埋め込み、
    /// プレハブ単体で完結させる。
    /// </summary>
    public static class FrozenAvatarBaker
    {
        /// <summary>動的生成アセットの永続化方式</summary>
        public enum AssetEmbedMode
        {
            /// <summary>Prefabのサブアセットとして埋め込む（1ファイルで完結）</summary>
            EmbedAsSubAssets,
            /// <summary>Prefabと同階層の "&lt;name&gt;_Frozen" フォルダに書き出す</summary>
            ExternalFolder,
        }

        public class FreezeReport
        {
            public string prefabPath;
            public int strippedComponentCount;
            public int embeddedMeshCount;
            public int embeddedMaterialCount;
            public int embeddedTextureCount;
            public readonly List<string> warnings = new List<string>();
            public readonly List<string> infos = new List<string>();
        }

        /// <summary>
        /// 除去するコンポーネントの型名。VRC SDKへのアセンブリ参照を持たないようリフレクションで判定する。
        /// 揺れ物・アニメーション・コンストレイント・コンタクトなど、ポーズを再変化させる要素を取り除く。
        /// （SkinnedMeshRenderer / MeshRenderer / MeshFilter / VRCAvatarDescriptor 等は保持する）
        /// </summary>
        private static readonly HashSet<string> StripTypeNames = new HashSet<string>
        {
            // アニメーション系
            "Animator", "Animation",
            // 物理・揺れ物系
            "Cloth", "VRCPhysBone", "VRCPhysBoneCollider",
            // コンタクト
            "VRCContactSender", "VRCContactReceiver",
            // ステーション・オーディオ
            "VRCStation", "VRCSpatialAudioSource", "VRCHeadChop",
            // Unityビルトインコンストレイント
            "ParentConstraint", "PositionConstraint", "RotationConstraint",
            "ScaleConstraint", "AimConstraint", "LookAtConstraint",
            // VRCコンストレイント
            "VRCConstraint", "VRCParentConstraint", "VRCPositionConstraint",
            "VRCRotationConstraint", "VRCScaleConstraint", "VRCAimConstraint",
            "VRCLookAtConstraint",
        };

        // ---------------------------------------------------------------
        // メイン処理
        // ---------------------------------------------------------------

        /// <summary>
        /// 再生中のアバターの現在状態を固定し、自己完結したPrefabとして保存する。
        /// 必ず再生中（EditorApplication.isPlaying）に呼ぶこと。
        /// </summary>
        public static FreezeReport Freeze(GameObject avatarRoot, string prefabPath, AssetEmbedMode embedMode)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));
            if (string.IsNullOrEmpty(prefabPath)) throw new ArgumentException("保存先パスが空です。", nameof(prefabPath));
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("再生中に実行してください。");

            var report = new FreezeReport { prefabPath = prefabPath };

            // 1. 再生中のアバターをクローン（現在のTRS・ブレンドシェイプ・enabled/activeSelfを複製）
            GameObject clone = Object.Instantiate(avatarRoot);
            clone.name = avatarRoot.name + "_Frozen";
            try
            {
                if (clone.scene != avatarRoot.scene && avatarRoot.scene.IsValid())
                    SceneManager.MoveGameObjectToScene(clone, avatarRoot.scene);

                // 2. プレハブ連結を解除して自己完結化
                if (PrefabUtility.IsPartOfAnyPrefab(clone))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                // 3. 揺れ物・アニメ系コンポーネントを除去（ポーズはクローン時点のTransformで確定済み）
                report.strippedComponentCount = StripDynamicComponents(clone);

                // 4. 保存先フォルダを用意
                string directory = Path.GetDirectoryName(prefabPath);
                if (!string.IsNullOrEmpty(directory)) EnsureFolder(directory.Replace('\\', '/'));

                // 5. プレハブを保存して資産ルートを得る。
                //    動的生成（非永続）の参照はこの時点でnull化されるが、cloneは生きたまま元参照を保持しているので、
                //    保存後にサブアセット化したコピーへ張り替える（AddObjectToAssetはプレハブ生成後にしか使えないため）。
                GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(clone, prefabPath, out bool success);
                if (!success || prefabRoot == null)
                    throw new InvalidOperationException($"プレハブの保存に失敗しました: {prefabPath}");

                // 6. ランタイム生成アセットを永続化し、プレハブ資産側の参照を張り替えて保存する。
                //    （SaveAsPrefabAsset→AddObjectToAsset→参照設定→SavePrefabAsset の順がサブアセット保持に確実）
                PersistAndRebind(clone, prefabRoot, prefabPath, embedMode, report);
                PrefabUtility.SavePrefabAsset(prefabRoot);
            }
            finally
            {
                // 一時クローンはシーンに残さない（再生終了時に破棄されるが明示的に消す）
                Object.DestroyImmediate(clone);
            }

            AssetDatabase.SaveAssets();
            return report;
        }

        // ---------------------------------------------------------------
        // 再生中の状態をそのまま静的メッシュ化（中間Prefabを介さない一気ベイク）
        // ---------------------------------------------------------------

        public class StaticBakeResult
        {
            public BakeReport report;
            /// <summary>アバターに設定済みのMeshBakeGroupを使ったか（falseなら自動グループ分け）</summary>
            public bool usedExistingGroup;
            public int groupCount;
        }

        /// <summary>
        /// 再生中のアバターの現在状態（揺れ物が落ち着いたポーズ）を、中間Prefabを介さず
        /// その場でStaticMeshBakerに通して静的メッシュ化する。
        ///
        /// 再生中（ビルド後）のアバターをクローンしてベイクするため、NDMFビルド前ではなく
        /// 「再生中に見えている状態そのもの」がベイクされる。
        /// アバター（またはその子）にMeshBakeGroupがあればその設定を使い、
        /// 無ければRendererGroupAnalyzerで全Rendererを自動グループ分けする。
        /// </summary>
        public static StaticBakeResult BakeStatic(GameObject avatarRoot, string outputDirectoryOverride)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("再生中に実行してください。");

            GameObject clone = Object.Instantiate(avatarRoot);
            clone.name = avatarRoot.name;
            try
            {
                if (clone.scene != avatarRoot.scene && avatarRoot.scene.IsValid())
                    SceneManager.MoveGameObjectToScene(clone, avatarRoot.scene);

                if (PrefabUtility.IsPartOfAnyPrefab(clone))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                // 揺れ物・アニメ系を除去（ポーズはクローン時点で確定済み。ベイク中の再ポーズを防ぐ）
                StripDynamicComponents(clone);

                MeshBakeGroup group = clone.GetComponentInChildren<MeshBakeGroup>(true);
                bool usedExisting = group != null && group.GetEffectiveGroups().Count > 0;

                if (!usedExisting)
                {
                    if (group == null) group = clone.AddComponent<MeshBakeGroup>();
                    ConfigureAutoGroups(clone, group, avatarRoot.name);
                    // 一時クローン上にシーンオブジェクトを作る意味は無いが、
                    // StaticMeshBakerはcreateSceneObject時のみPrefabを書き出すため有効にする。
                    group.createSceneObject = true;
                }

                if (!string.IsNullOrEmpty(outputDirectoryOverride))
                    group.outputDirectory = outputDirectoryOverride;
                if (string.IsNullOrEmpty(group.outputName))
                    group.outputName = avatarRoot.name;

                BakeReport report = StaticMeshBaker.Bake(group);
                return new StaticBakeResult
                {
                    report = report,
                    usedExistingGroup = usedExisting,
                    groupCount = group.GetEffectiveGroups().Count,
                };
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        /// <summary>クローン配下の全Renderer（SkinnedMeshRenderer/MeshRenderer）を自動グループ分けして設定する</summary>
        private static void ConfigureAutoGroups(GameObject clone, MeshBakeGroup group, string outputName)
        {
            var renderers = new List<Renderer>();
            foreach (SkinnedMeshRenderer smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null) renderers.Add(smr);
            }
            foreach (MeshRenderer mr in clone.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) renderers.Add(mr);
            }

            List<RendererGroupAnalyzer.Proposal> proposals = RendererGroupAnalyzer.Analyze(
                renderers, !group.mergeMaterials, RendererGroupAnalyzer.DefaultVertexBudget);

            group.rendererGroups = new List<BakeRendererGroup>(proposals.Count);
            foreach (RendererGroupAnalyzer.Proposal proposal in proposals)
            {
                var g = new BakeRendererGroup { name = proposal.name };
                g.renderers.AddRange(proposal.renderers);
                group.rendererGroups.Add(g);
            }

            if (string.IsNullOrEmpty(group.outputName)) group.outputName = outputName;
        }

        // ---------------------------------------------------------------
        // コンポーネント除去
        // ---------------------------------------------------------------

        private static int StripDynamicComponents(GameObject cloneRoot)
        {
            int count = 0;
            // 子から先に消すと安全（依存関係でDestroyが弾かれるのを避ける）
            foreach (Component component in cloneRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue; // Missing Script
                if (!ShouldStrip(component.GetType())) continue;
                Object.DestroyImmediate(component, true);
                count++;
            }
            return count;
        }

        /// <summary>型名（基底型も辿る）が除去対象に一致するか</summary>
        private static bool ShouldStrip(Type type)
        {
            for (Type t = type; t != null && t != typeof(Component); t = t.BaseType)
            {
                if (StripTypeNames.Contains(t.Name)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------
        // 動的生成アセットの永続化
        // ---------------------------------------------------------------

        /// <summary>
        /// cloneRoot（生きている＝ランタイム参照を保持）が参照する非永続のメッシュ/マテリアル/テクスチャを
        /// 永続化し、保存済みプレハブ資産（prefabRoot）の対応するRenderer/MeshFilterへ張り替える。
        ///
        /// プレハブ保存時に非永続参照はnull化されるため、cloneRootから元参照を読み取り、
        /// 同一階層・同一列挙順となるprefabRoot側へインデックス対応で割り当てる。
        /// 参照先が先に永続化されている必要があるため、テクスチャ→マテリアル→メッシュの順で永続化する。
        /// </summary>
        private static void PersistAndRebind(
            GameObject cloneRoot, GameObject prefabRoot, string prefabPath,
            AssetEmbedMode mode, FreezeReport report)
        {
            var cloneRenderers = cloneRoot.GetComponentsInChildren<Renderer>(true);
            var prefabRenderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
            var cloneFilters = cloneRoot.GetComponentsInChildren<MeshFilter>(true);
            var prefabFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);

            if (cloneRenderers.Length != prefabRenderers.Length || cloneFilters.Length != prefabFilters.Length)
            {
                report.warnings.Add("クローンと保存プレハブの階層が一致しないため、動的生成アセットの埋め込みをスキップしました。");
                return;
            }

            string externalFolder = null;
            if (mode == AssetEmbedMode.ExternalFolder)
            {
                string dir = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
                externalFolder = $"{dir}/{Path.GetFileNameWithoutExtension(prefabPath)}_Frozen";
                EnsureFolder(externalFolder);
            }

            // --- 収集（instanceIDで重複排除）。cloneから元参照を読む ---
            var meshes = new Dictionary<int, Mesh>();
            var materials = new Dictionary<int, Material>();
            var textures = new Dictionary<int, Texture>();

            void CollectMesh(Mesh m)
            {
                if (m != null && !EditorUtility.IsPersistent(m)) meshes[m.GetInstanceID()] = m;
            }
            void CollectMaterial(Material mat)
            {
                if (mat == null || EditorUtility.IsPersistent(mat)) return;
                materials[mat.GetInstanceID()] = mat;
                foreach (int id in mat.GetTexturePropertyNameIDs())
                {
                    if (!mat.HasProperty(id)) continue;
                    Texture tex = mat.GetTexture(id);
                    if (tex != null && !EditorUtility.IsPersistent(tex)) textures[tex.GetInstanceID()] = tex;
                }
            }

            foreach (Renderer r in cloneRenderers)
            {
                if (r is SkinnedMeshRenderer smr) CollectMesh(smr.sharedMesh);
                foreach (Material mat in r.sharedMaterials) CollectMaterial(mat);
            }
            foreach (MeshFilter mf in cloneFilters) CollectMesh(mf.sharedMesh);

            // --- フェーズA: テクスチャ ---
            var textureMap = new Dictionary<int, Texture>();
            foreach (KeyValuePair<int, Texture> kv in textures)
            {
                Texture2D copy = MakePersistentTextureCopy(kv.Value, report);
                if (copy == null) continue;
                copy.name = string.IsNullOrEmpty(kv.Value.name) ? "FrozenTexture" : kv.Value.name;
                var persisted = (Texture)Persist(copy, mode, prefabPath, externalFolder, "png");
                if (persisted == null) continue;
                textureMap[kv.Key] = persisted;
                report.embeddedTextureCount++;
            }

            // --- フェーズB: マテリアル（テクスチャ参照を張り替えてから永続化） ---
            var materialMap = new Dictionary<int, Material>();
            foreach (KeyValuePair<int, Material> kv in materials)
            {
                Material src = kv.Value;
                var copy = new Material(src) { name = string.IsNullOrEmpty(src.name) ? "FrozenMaterial" : src.name };
                foreach (int id in copy.GetTexturePropertyNameIDs())
                {
                    if (!copy.HasProperty(id)) continue;
                    Texture tex = copy.GetTexture(id);
                    if (tex != null && textureMap.TryGetValue(tex.GetInstanceID(), out Texture mapped))
                        copy.SetTexture(id, mapped);
                }
                var persisted = (Material)Persist(copy, mode, prefabPath, externalFolder, "mat");
                materialMap[kv.Key] = persisted;
                report.embeddedMaterialCount++;
            }

            // --- フェーズC: メッシュ ---
            var meshMap = new Dictionary<int, Mesh>();
            foreach (KeyValuePair<int, Mesh> kv in meshes)
            {
                Mesh src = kv.Value;
                if (!src.isReadable)
                {
                    report.warnings.Add($"メッシュ「{src.name}」はRead/Write無効のため埋め込めませんでした（参照を維持します）。");
                    continue;
                }
                var copy = Object.Instantiate(src);
                copy.name = string.IsNullOrEmpty(src.name) ? "FrozenMesh" : src.name;
                var persisted = (Mesh)Persist(copy, mode, prefabPath, externalFolder, "asset");
                meshMap[kv.Key] = persisted;
                report.embeddedMeshCount++;
            }

            // --- フェーズD: cloneの元参照を見て、prefabRoot側へインデックス対応で張り替える ---
            // 元がプロジェクトアセットだった参照（マップに無いもの）はクローン側の参照をそのまま使う
            //（プレハブ保存で正しく解決済みだが、null化された動的参照のみ確実に復元したいので両方を上書きする）。
            Material[] ResolveMaterials(Material[] cloneMats)
            {
                var resolved = new Material[cloneMats.Length];
                for (int i = 0; i < cloneMats.Length; i++)
                {
                    Material cm = cloneMats[i];
                    resolved[i] = (cm != null && materialMap.TryGetValue(cm.GetInstanceID(), out Material nm)) ? nm : cm;
                }
                return resolved;
            }

            for (int i = 0; i < cloneRenderers.Length; i++)
            {
                if (cloneRenderers[i] is SkinnedMeshRenderer cloneSmr
                    && prefabRenderers[i] is SkinnedMeshRenderer prefabSmr)
                {
                    Mesh cm = cloneSmr.sharedMesh;
                    if (cm != null && meshMap.TryGetValue(cm.GetInstanceID(), out Mesh nm))
                        prefabSmr.sharedMesh = nm;
                }
                prefabRenderers[i].sharedMaterials = ResolveMaterials(cloneRenderers[i].sharedMaterials);
            }

            for (int i = 0; i < cloneFilters.Length; i++)
            {
                Mesh cm = cloneFilters[i].sharedMesh;
                if (cm != null && meshMap.TryGetValue(cm.GetInstanceID(), out Mesh nm))
                    prefabFilters[i].sharedMesh = nm;
            }
        }

        /// <summary>
        /// 動的生成アセットを永続化し、参照張り替えに使う永続オブジェクトを返す。
        /// 埋め込みモードならPrefabのサブアセットとして、外部フォルダモードなら個別ファイルとして保存する。
        /// テクスチャの外部保存はPNGを書き出してインポートするため、ロードし直したアセットを返す。
        /// </summary>
        private static Object Persist(
            Object asset, AssetEmbedMode mode, string prefabPath, string externalFolder, string extension)
        {
            if (mode == AssetEmbedMode.EmbedAsSubAssets)
            {
                AssetDatabase.AddObjectToAsset(asset, prefabPath);
                return asset;
            }

            // 外部フォルダ: テクスチャはPNG、それ以外は.asset/.matとして保存する
            string baseName = MakeSafeFileName(asset.name);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{externalFolder}/{baseName}.{extension}");
            if (asset is Texture2D tex)
            {
                string absolutePath = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath));
                File.WriteAllBytes(absolutePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(assetPath);
                // PNGを経由するとオブジェクトが入れ替わるため、参照はインポート済みアセットを使う
                return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }

            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        // ---------------------------------------------------------------
        // テクスチャのコピー
        // ---------------------------------------------------------------

        /// <summary>
        /// テクスチャを永続化可能な読み取り可能Texture2Dとして複製する。
        /// readableなTexture2DはInstantiateでコピーし、それ以外（RenderTexture/非readable）は
        /// Graphics.Blit→ReadPixelsでGPUから読み戻す。
        /// </summary>
        private static Texture2D MakePersistentTextureCopy(Texture src, FreezeReport report)
        {
            if (src is Texture2D tex2D && tex2D.isReadable)
            {
                return Object.Instantiate(tex2D);
            }

            int width = Mathf.Max(1, src.width);
            int height = Mathf.Max(1, src.height);
            RenderTexture rt = RenderTexture.GetTemporary(
                width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var result = new Texture2D(width, height, TextureFormat.RGBA32, true);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                return result;
            }
            catch (Exception e)
            {
                report.warnings.Add($"テクスチャ「{src.name}」のコピーに失敗しました: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // ---------------------------------------------------------------
        // ユーティリティ
        // ---------------------------------------------------------------

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Asset";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>"Assets/A/B" のようなフォルダパスを順に作成する（StaticMeshBakerと同様）</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] segments = path.Split('/');
            string current = segments[0]; // 通常 "Assets"
            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }
                current = next;
            }
        }
    }
}
