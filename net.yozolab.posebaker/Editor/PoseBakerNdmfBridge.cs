using UnityEngine;

#if YOZOLAB_NDMF
using System.Collections.Generic;
using UnityEditor;
using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(YozoLab.PoseBaker.PoseBakerNdmfPlugin))]
#endif

namespace YozoLab.PoseBaker
{
#if YOZOLAB_NDMF
    /// <summary>
    /// NDMFのObjectRegistryを使い、ビルド後（再生中）のTransformからビルド前のシーン上のパスを取得するブリッジ。
    ///
    /// Resolvingフェーズ（各ツールによる変形の前）で全Transform/GameObjectのObjectReferenceを
    /// 作成しておくことで「ビルド前のパス」がレジストリに確定する。以降のフェーズで
    /// 移動（Modular AvatarのMerge Armature等）や置換（RegisterReplacedObject）された
    /// オブジェクトも、キャプチャ時に元のパスへ辿れる。
    /// </summary>
    internal static class PoseBakerNdmfBridge
    {
        public static bool Available => true;

        // アバタールート（再生中のインスタンス） → そのビルドのObjectRegistry
        private static readonly Dictionary<GameObject, IObjectRegistry> Registries =
            new Dictionary<GameObject, IObjectRegistry>();

        [InitializeOnLoadMethod]
        private static void Init()
        {
            // ドメインリロード無効設定でも前回セッションの参照が残らないように掃除する
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode ||
                    state == PlayModeStateChange.ExitingEditMode)
                {
                    Registries.Clear();
                }
            };
        }

        internal static void OnResolving(BuildContext context)
        {
            Registries[context.AvatarRootObject] = ObjectRegistry.ActiveRegistry;

            // この時点（変形前）の階層パスで参照を確定させる
            foreach (Transform t in context.AvatarRootTransform.GetComponentsInChildren<Transform>(true))
            {
                ObjectRegistry.GetReference(t);
                ObjectRegistry.GetReference(t.gameObject);
            }
        }

        /// <summary>
        /// ビルド前のシーン絶対パスを取得する。レジストリがない（NDMFが処理していない）
        /// アバターや、ビルド中に新規生成されたオブジェクトに対してはfalseを返す。
        /// </summary>
        public static bool TryGetOriginalPath(GameObject avatarRoot, Transform target, out string originalPath)
        {
            originalPath = null;
            if (avatarRoot == null || target == null) return false;
            if (!Registries.TryGetValue(avatarRoot, out IObjectRegistry registry) || registry == null) return false;

            using (new ObjectRegistryScope(registry))
            {
                // 置換の登録はGameObject単位で行われることが多いためそちらを優先し、
                // 次にTransform自体の参照を見る
                string path = ObjectRegistry.GetReference(target.gameObject)?.Path;
                if (string.IsNullOrEmpty(path))
                {
                    path = ObjectRegistry.GetReference(target)?.Path;
                }
                originalPath = path;
            }

            return !string.IsNullOrEmpty(originalPath);
        }
    }

    internal class PoseBakerNdmfPlugin : Plugin<PoseBakerNdmfPlugin>
    {
        public override string QualifiedName => "net.yozolab.posebaker";
        public override string DisplayName => "PlayMode Pose Baker";

        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving)
                .Run("Pin original transform paths", PoseBakerNdmfBridge.OnResolving);
        }
    }
#else
    /// <summary>NDMF未導入環境向けのスタブ。常に取得失敗を返し、パス照合にフォールバックさせる。</summary>
    internal static class PoseBakerNdmfBridge
    {
        public static bool Available => false;

        public static bool TryGetOriginalPath(GameObject avatarRoot, Transform target, out string originalPath)
        {
            originalPath = null;
            return false;
        }
    }
#endif
}
