using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YozoLab.PoseBaker
{
    /// <summary>
    /// キャプチャ対象の範囲
    /// </summary>
    public enum CaptureScope
    {
        /// <summary>アバター配下の全Transform</summary>
        AllTransforms,
        /// <summary>PhysBoneの影響下にあるボーンのみ</summary>
        PhysBonesOnly,
    }

    [Serializable]
    public class CapturedTransform
    {
        public string path;
        /// <summary>NDMF ObjectRegistryから取得したビルド前の相対パス（取得できなかった場合は空）</summary>
        public string originalPath;
        public string name;
        /// <summary>AnimatorのHumanoidマッピング対象ボーンかどうか（AnimationClip書き出し時に除外される）</summary>
        public bool isHumanoidBone;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    [Serializable]
    public class CaptureData
    {
        public string avatarRootName;
        public string avatarScenePath;
        public string sceneName;
        public string capturedAt;
        public int captureScope;
        /// <summary>ObjectRegistryでビルド前パスを取得できたTransform数</summary>
        public int originalPathCount;
        public CapturedTransform[] transforms;
    }

    public class ApplyResult
    {
        public int appliedCount;
        public int unchangedCount;
    }

    /// <summary>
    /// 再生中のアバターのボーンTransformをキャプチャし、再生終了後のEditモードに適用するコア処理。
    ///
    /// NDMF(Apply on Play)はビルド前後でGlobalObjectIdが安定しないため、
    /// アバタールートからの相対階層パスでボーンを照合する。
    /// 再生終了時のドメインリロードを跨ぐため、キャプチャ結果はLibrary配下のJSONに保存する。
    /// </summary>
    public static class PlayModePoseBaker
    {
        private static string StorageDirectory
        {
            get
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.Combine(projectRoot, "Library", "YozoLab", "PoseBaker");
            }
        }

        public static string StoragePath => Path.Combine(StorageDirectory, "LastCapture.json");

        // ---------------------------------------------------------------
        // キャプチャ
        // ---------------------------------------------------------------

        public static CaptureData Capture(GameObject avatarRoot, CaptureScope scope)
        {
            Transform root = avatarRoot.transform;
            IEnumerable<Transform> targets;

            if (scope == CaptureScope.PhysBonesOnly)
            {
                targets = CollectPhysBoneTransforms(root);
            }
            else
            {
                // ルート自身は含めない（再生中の移動位置を持ち込まないため）
                targets = root.GetComponentsInChildren<Transform>(true).Where(t => t != root);
            }

            // NDMFが処理したアバターなら、ObjectRegistryからビルド前のルートパスを取得しておく
            string rootOriginalAbsolutePath = null;
            if (PoseBakerNdmfBridge.TryGetOriginalPath(avatarRoot, root, out string rootPath))
            {
                rootOriginalAbsolutePath = rootPath;
            }

            HashSet<Transform> humanoidBones = CollectHumanoidBones(avatarRoot);

            int originalPathCount = 0;
            var entries = new List<CapturedTransform>();
            foreach (Transform t in targets)
            {
                if (t == root) continue;

                string originalPath = ResolveOriginalRelativePath(avatarRoot, rootOriginalAbsolutePath, t);
                if (!string.IsNullOrEmpty(originalPath)) originalPathCount++;

                entries.Add(new CapturedTransform
                {
                    path = BuildRelativePath(root, t),
                    originalPath = originalPath,
                    name = t.name,
                    isHumanoidBone = humanoidBones.Contains(t),
                    localPosition = t.localPosition,
                    localRotation = t.localRotation,
                    localScale = t.localScale,
                });
            }

            return new CaptureData
            {
                avatarRootName = avatarRoot.name,
                avatarScenePath = BuildScenePath(root),
                sceneName = avatarRoot.scene.name,
                capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                captureScope = (int)scope,
                originalPathCount = originalPathCount,
                transforms = entries.ToArray(),
            };
        }

        /// <summary>
        /// AnimatorのHumanoidマッピング対象ボーン（指・目・顎含む）を収集する。
        /// 再生中のキャプチャで呼ぶため、ビルド後のAnimatorのマッピングが参照できる。
        /// </summary>
        private static HashSet<Transform> CollectHumanoidBones(GameObject avatarRoot)
        {
            var result = new HashSet<Transform>();
            foreach (Animator animator in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (animator.avatar == null || !animator.avatar.isHuman) continue;
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone != null) result.Add(bone);
                }
            }
            return result;
        }

        /// <summary>
        /// ObjectRegistryから取得したビルド前のシーン絶対パスを、アバタールート基準の相対パスに変換する。
        /// 取得できない場合や、ビルド前はアバター外にあったオブジェクトの場合はnullを返す。
        /// </summary>
        private static string ResolveOriginalRelativePath(
            GameObject avatarRoot, string rootOriginalAbsolutePath, Transform target)
        {
            if (rootOriginalAbsolutePath == null) return null;
            if (!PoseBakerNdmfBridge.TryGetOriginalPath(avatarRoot, target, out string absolutePath)) return null;

            string prefix = rootOriginalAbsolutePath + "/";
            if (!absolutePath.StartsWith(prefix, StringComparison.Ordinal)) return null;
            return absolutePath.Substring(prefix.Length);
        }

        public static void Save(CaptureData data)
        {
            Directory.CreateDirectory(StorageDirectory);
            File.WriteAllText(StoragePath, JsonUtility.ToJson(data));
        }

        public static CaptureData Load()
        {
            if (!File.Exists(StoragePath)) return null;
            try
            {
                var data = JsonUtility.FromJson<CaptureData>(File.ReadAllText(StoragePath));
                return (data != null && data.transforms != null) ? data : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PoseBaker] キャプチャデータの読み込みに失敗しました: {e.Message}");
                return null;
            }
        }

        public static DateTime? GetStorageTimestamp()
        {
            return File.Exists(StoragePath) ? File.GetLastWriteTimeUtc(StoragePath) : (DateTime?)null;
        }

        // ---------------------------------------------------------------
        // 適用
        // ---------------------------------------------------------------

        /// <summary>
        /// AnimationClipをサンプリングした結果のTransformをシーン上のアバターに焼き込む。
        /// AnimationMode（Animationウィンドウのプレビューと同じ仕組み）でサンプリングするため、
        /// HumanoidクリップはAnimatorのAvatar経由でマッスルカーブがボーン回転に解決され、
        /// Genericクリップはバインディングパスで解決される。どちらも同じ手順で処理できる。
        /// </summary>
        public static ApplyResult ApplyAnimationClip(
            GameObject avatarRoot, AnimationClip clip, float time,
            bool applyPosition, bool applyRotation, bool applyScale)
        {
            Transform root = avatarRoot.transform;
            Transform[] transforms = avatarRoot.GetComponentsInChildren<Transform>(true);
            var sampledPositions = new Vector3[transforms.Length];
            var sampledRotations = new Quaternion[transforms.Length];
            var sampledScales = new Vector3[transforms.Length];

            // 一時的にプレビューとしてサンプリングし、結果のTRSだけ控えてから元の状態に戻す
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(avatarRoot, clip, time);
                AnimationMode.EndSampling();

                for (int i = 0; i < transforms.Length; i++)
                {
                    sampledPositions[i] = transforms[i].localPosition;
                    sampledRotations[i] = transforms[i].localRotation;
                    sampledScales[i] = transforms[i].localScale;
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            var result = new ApplyResult();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Bake Animation Clip Pose");
            int undoGroup = Undo.GetCurrentGroup();

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform target = transforms[i];
                // ルート自体は動かさない（ルートモーション等の持ち込みを防ぐ）
                if (target == root) continue;

                bool changed =
                    (applyPosition && (target.localPosition - sampledPositions[i]).sqrMagnitude > 1e-12f) ||
                    (applyRotation && Quaternion.Angle(target.localRotation, sampledRotations[i]) > 1e-4f) ||
                    (applyScale && (target.localScale - sampledScales[i]).sqrMagnitude > 1e-12f);

                if (!changed)
                {
                    result.unchangedCount++;
                    continue;
                }

                Undo.RecordObject(target, "Bake Animation Clip Pose");
                if (applyPosition) target.localPosition = sampledPositions[i];
                if (applyRotation) target.localRotation = sampledRotations[i];
                if (applyScale) target.localScale = sampledScales[i];
                result.appliedCount++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            return result;
        }

        // ---------------------------------------------------------------
        // AnimationClip書き出し
        // ---------------------------------------------------------------

        /// <summary>
        /// キャプチャ結果を1キーのAnimationClipとして生成する。
        /// Humanoidマッピング対象のボーンは、TransformカーブがHumanoidのマッスル制御と
        /// 競合するため書き出さない（素体のポーズはHumanoidクリップ側で扱う）。
        /// アニメーションのバインディングパスには同名兄弟の区別("名前[n]")が使えないため、
        /// 2番目以降の同名兄弟もスキップし、それぞれの数をoutで返す。
        /// </summary>
        public static AnimationClip CreateAnimationClip(
            CaptureData data,
            bool includePosition, bool includeRotation, bool includeScale,
            out int skippedDuplicateCount, out int skippedHumanoidCount)
        {
            var clip = new AnimationClip();
            skippedDuplicateCount = 0;
            skippedHumanoidCount = 0;

            foreach (CapturedTransform entry in data.transforms)
            {
                if (entry.isHumanoidBone)
                {
                    skippedHumanoidCount++;
                    continue;
                }

                string plainPath = StripAllSegmentIndices(entry.path, out bool hasDuplicateSegment);

                // ビルド中に移動されたボーンは、Editモードの階層に合わせてビルド前パスでバインドする
                string animPath;
                if (!string.IsNullOrEmpty(entry.originalPath) && entry.originalPath != plainPath)
                {
                    animPath = entry.originalPath;
                }
                else
                {
                    if (hasDuplicateSegment)
                    {
                        skippedDuplicateCount++;
                        continue;
                    }
                    animPath = plainPath;
                }

                if (includePosition)
                {
                    SetConstantCurve(clip, animPath, "m_LocalPosition.x", entry.localPosition.x);
                    SetConstantCurve(clip, animPath, "m_LocalPosition.y", entry.localPosition.y);
                    SetConstantCurve(clip, animPath, "m_LocalPosition.z", entry.localPosition.z);
                }
                if (includeRotation)
                {
                    SetConstantCurve(clip, animPath, "m_LocalRotation.x", entry.localRotation.x);
                    SetConstantCurve(clip, animPath, "m_LocalRotation.y", entry.localRotation.y);
                    SetConstantCurve(clip, animPath, "m_LocalRotation.z", entry.localRotation.z);
                    SetConstantCurve(clip, animPath, "m_LocalRotation.w", entry.localRotation.w);
                }
                if (includeScale)
                {
                    SetConstantCurve(clip, animPath, "m_LocalScale.x", entry.localScale.x);
                    SetConstantCurve(clip, animPath, "m_LocalScale.y", entry.localScale.y);
                    SetConstantCurve(clip, animPath, "m_LocalScale.z", entry.localScale.z);
                }
            }

            return clip;
        }

        private static void SetConstantCurve(AnimationClip clip, string path, string propertyName, float value)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(new Keyframe(0f, value)));
        }

        /// <summary>
        /// 生成したクリップをアセットとして保存する。
        /// 同じパスに既存のクリップがある場合はGUIDを維持したまま中身を差し替える。
        /// </summary>
        public static AnimationClip SaveAnimationClip(AnimationClip clip, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                AssetDatabase.SaveAssets();
                return existing;
            }

            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();
            return clip;
        }

        // ---------------------------------------------------------------
        // 階層パス
        // ---------------------------------------------------------------

        /// <summary>
        /// ルートからの相対パスを構築する。
        /// 同名の兄弟が存在する場合は "名前[n]"（n = 同名兄弟内での出現順）で区別する。
        /// 絶対インデックスではなく同名内の出現順を使うことで、
        /// NDMFによる別名オブジェクトの追加・並べ替えの影響を受けにくくする。
        /// </summary>
        public static string BuildRelativePath(Transform root, Transform target)
        {
            var segments = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Add(BuildSegment(current));
                current = current.parent;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string BuildSegment(Transform t)
        {
            Transform parent = t.parent;
            if (parent == null) return t.name;

            int duplicateIndex = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling == t) break;
                if (sibling.name == t.name) duplicateIndex++;
            }
            return duplicateIndex > 0 ? $"{t.name}[{duplicateIndex}]" : t.name;
        }

        /// <summary>パス内の全セグメントから同名兄弟インデックス("名前[n]")を取り除く</summary>
        private static string StripAllSegmentIndices(string path, out bool hadDuplicateSegment)
        {
            hadDuplicateSegment = false;
            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = StripSegmentIndex(segments[i], out int duplicateIndex);
                if (duplicateIndex > 0) hadDuplicateSegment = true;
            }
            return string.Join("/", segments);
        }

        /// <summary>"名前[n]" 形式のセグメントから名前と同名兄弟インデックスを取り出す</summary>
        private static string StripSegmentIndex(string segment, out int duplicateIndex)
        {
            duplicateIndex = 0;
            if (segment.EndsWith("]"))
            {
                int open = segment.LastIndexOf('[');
                if (open > 0 && int.TryParse(segment.Substring(open + 1, segment.Length - open - 2), out int parsed))
                {
                    duplicateIndex = parsed;
                    return segment.Substring(0, open);
                }
            }
            return segment;
        }

        /// <summary>シーンルートからのアバタールートの階層パス（再生終了後の再検索用）</summary>
        public static string BuildScenePath(Transform t)
        {
            var segments = new List<string>();
            Transform current = t;
            while (current != null)
            {
                segments.Add(current.name);
                current = current.parent;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        /// <summary>ロード済みの全シーンから階層パスでGameObjectを探す</summary>
        public static GameObject FindByScenePath(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return null;
            string[] segments = scenePath.Split('/');

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (GameObject rootObject in scene.GetRootGameObjects())
                {
                    if (rootObject.name != segments[0]) continue;

                    Transform current = rootObject.transform;
                    bool ok = true;
                    for (int s = 1; s < segments.Length; s++)
                    {
                        current = current.Find(segments[s]);
                        if (current == null) { ok = false; break; }
                    }
                    if (ok) return current.gameObject;
                }
            }
            return null;
        }

        // ---------------------------------------------------------------
        // PhysBone収集（VRC SDKへのアセンブリ参照を持たないようリフレクションで解決）
        // ---------------------------------------------------------------

        public static HashSet<Transform> CollectPhysBoneTransforms(Transform root)
        {
            var result = new HashSet<Transform>();

            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue; // Missing Script
                Type type = component.GetType();
                if (type.Name != "VRCPhysBone") continue;

                var pbRoot = type.GetField("rootTransform")?.GetValue(component) as Transform;
                if (pbRoot == null) pbRoot = component.transform;

                var ignoreSet = new HashSet<Transform>();
                if (type.GetField("ignoreTransforms")?.GetValue(component) is System.Collections.IEnumerable ignores)
                {
                    foreach (object ignore in ignores)
                    {
                        if (ignore is Transform ignoreTransform) ignoreSet.Add(ignoreTransform);
                    }
                }

                AddSubtree(pbRoot, ignoreSet, result);
            }

            return result;
        }

        private static void AddSubtree(Transform t, HashSet<Transform> ignoreSet, HashSet<Transform> result)
        {
            if (ignoreSet.Contains(t)) return;
            result.Add(t);
            for (int i = 0; i < t.childCount; i++)
            {
                AddSubtree(t.GetChild(i), ignoreSet, result);
            }
        }
    }
}
