using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 代替ギズモパスへ描画内容を差し込むための拡張点。
    ///
    /// パスが PhysBone を 1 つ組み立てるたびに <see cref="Build"/> が呼ばれる
    /// （組み立てはシーンビューの Repaint ごと）。<paramref name="canvas"/> へ
    /// 図形を足すか、<see cref="PhysBoneGizmoCanvas.SuppressDefault"/> で
    /// 既定形状を消せる。
    /// </summary>
    public interface IPhysBoneGizmoExtension
    {
        /// <summary>呼び出し順。小さいものが先。</summary>
        int Order { get; }

        /// <param name="physBone">対象の PhysBone（VRC.Dynamics.VRCPhysBoneBase）。</param>
        /// <param name="canvas">描き込み先。</param>
        void Build(Component physBone, PhysBoneGizmoCanvas canvas);
    }

    /// <summary>
    /// PhysBone ギズモの代替描画パス。
    ///
    /// SDK 自身の描画は <see cref="SdkGizmoSuppressor"/>（Harmony）で止め、
    /// 選択に関連する PhysBone だけをここで一括バッチ描画する。
    /// 組み立ての本体は PhysBoneGizmoDriver（SDK があるときだけコンパイルされる）
    /// にある。このクラスは SDK の有無によらず存在する公開面。
    /// </summary>
    public static class PhysBoneGizmoPass
    {
        private static readonly List<IPhysBoneGizmoExtension> Registered =
            new List<IPhysBoneGizmoExtension>();

        internal static IReadOnlyList<IPhysBoneGizmoExtension> Extensions => Registered;

        /// <summary>パスが実際に動いているか（機能が ON で、SDK がある）。</summary>
        public static bool Active
        {
#if YOZOLAB_VRCGIZMOACC_VRCSDK
            get { return VRCGizmoAcceleratorSettings.instance.enabled; }
#else
            get { return false; }
#endif
        }

        public static void Register(IPhysBoneGizmoExtension extension)
        {
            if (extension == null || Registered.Contains(extension)) return;
            Registered.Add(extension);
            Registered.Sort((a, b) => a.Order.CompareTo(b.Order));
            Invalidate();
        }

        public static void Unregister(IPhysBoneGizmoExtension extension)
        {
            if (Registered.Remove(extension)) Invalidate();
        }

        /// <summary>
        /// 描き直しを頼む。組み立ては Repaint ごとに行われるので、
        /// 実体はシーンビューの再描画要求でしかない。
        /// </summary>
        public static void Invalidate() => SceneView.RepaintAll();
    }
}

#if YOZOLAB_VRCGIZMOACC_VRCSDK
namespace YozoLab.VRCGizmoAccelerator
{
    using System.Diagnostics;
    using VRC.Dynamics;

    /// <summary>
    /// パスの本体。シーンビューの Repaint ごとに
    ///   1) SDK のギズモ入口を Harmony で止める（冪等）
    ///   2) 選択に関連する PhysBone を選び出す
    ///   3) 拡張 → 既定形状の順で組み立てる
    ///   4) 溜めた頂点を SetPass 1 回の即時描画で流す
    /// を行う。キャッシュは持たない。毎回組み立てても、対象が選択まわりに
    /// 絞られている限り SDK の全数描画よりはるかに軽い。
    /// </summary>
    [InitializeOnLoad]
    internal static class PhysBoneGizmoDriver
    {
        private static readonly PhysBoneGizmoCanvas Canvas = new PhysBoneGizmoCanvas();

        private struct Target
        {
            public VRCPhysBoneBase physBone;
            public bool selected;
        }

        private static readonly List<Target> Targets = new List<Target>();
        private static readonly List<VRCPhysBoneBase> ComponentBuffer = new List<VRCPhysBoneBase>();

        private static bool _wasActive;

        // ウィンドウ表示用
        internal static int LastTargetCount { get; private set; }
        internal static int LastVertexCount { get; private set; }
        internal static double LastBuildMs { get; private set; }

        static PhysBoneGizmoDriver()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload += SdkGizmoSuppressor.Uninstall;
        }

        private static void OnSceneGUI(SceneView view)
        {
            var settings = VRCGizmoAcceleratorSettings.instance;

            if (!settings.enabled)
            {
                if (_wasActive) Deactivate();
                return;
            }

            _wasActive = true;
            SdkGizmoSuppressor.Install();

            if (Event.current.type != EventType.Repaint) return;

            // シーンビューのギズモ全体トグル。OFF なら SDK のギズモも呼ばれて
            // いないはずなので、こちらも何も描かない（組み立てもしない）。
            if (!view.drawGizmos)
            {
                LastTargetCount = 0;
                LastVertexCount = 0;
                return;
            }

            CollectTargets(settings.drawUnselected);
            Rebuild();
            Draw();
        }

        private static void Deactivate()
        {
            _wasActive = false;
            Targets.Clear();
            Canvas.Clear();
            SdkGizmoSuppressor.Uninstall();
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------
        // 対象の選び出し
        // ---------------------------------------------------------------

        /// <summary>
        /// 描く PhysBone を集める。SDK のギズモと同じ見え方にする。
        ///   ・選択階層の配下にある PhysBone は全て描く。選択 GameObject の
        ///     真上に載っているものだけ不透明、それ以外は半透明。
        ///   ・ボーンを直接つまんでいるときは、そのチェーンを持つ上位の
        ///     PhysBone も出す（半透明）。
        ///   ・選択が無ければ何も描かない。
        /// drawUnselected のときは残り全部も半透明の対象に足す。
        /// </summary>
        private static void CollectTargets(bool drawUnselected)
        {
            Targets.Clear();

            var seen = new HashSet<VRCPhysBoneBase>();
            GameObject[] selection = Selection.gameObjects;
            IReadOnlyList<VRCPhysBoneBase> all = PhysBoneScanner.All;

            if (selection.Length > 0)
            {
                // 選択階層の配下
                for (int i = 0; i < all.Count; i++)
                {
                    VRCPhysBoneBase pb = all[i];
                    if (!IsDrawable(pb)) continue;

                    bool underSelection = false;
                    bool selectedSelf = false;
                    for (int j = 0; j < selection.Length; j++)
                    {
                        GameObject go = selection[j];
                        if (go == null) continue;

                        if (pb.gameObject == go)
                        {
                            selectedSelf = true;
                            underSelection = true;
                            break;
                        }
                        if (pb.transform.IsChildOf(go.transform)) underSelection = true;
                    }

                    if (underSelection)
                    {
                        seen.Add(pb);
                        Targets.Add(new Target { physBone = pb, selected = selectedSelf });
                    }
                }

                // ボーンを直接つまんでいるとき、そのチェーンを持つ上位の PhysBone
                for (int j = 0; j < selection.Length; j++)
                {
                    GameObject go = selection[j];
                    if (go == null) continue;
                    Transform selected = go.transform;

                    for (Transform ancestor = selected.parent; ancestor != null; ancestor = ancestor.parent)
                    {
                        ancestor.GetComponents(ComponentBuffer);
                        for (int i = 0; i < ComponentBuffer.Count; i++)
                        {
                            VRCPhysBoneBase pb = ComponentBuffer[i];
                            if (!IsDrawable(pb) || seen.Contains(pb)) continue;

                            Transform root = pb.GetRootTransform();
                            if (root != null && selected.IsChildOf(root))
                            {
                                seen.Add(pb);
                                Targets.Add(new Target { physBone = pb, selected = false });
                            }
                        }
                    }
                }
                ComponentBuffer.Clear();
            }

            if (drawUnselected)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    VRCPhysBoneBase pb = all[i];
                    if (!IsDrawable(pb) || seen.Contains(pb)) continue;
                    Targets.Add(new Target { physBone = pb, selected = false });
                }
            }

            LastTargetCount = Targets.Count;
        }

        /// <summary>
        /// showGizmos はユーザーの設定として残してあるので、そのまま尊重する。
        /// Gizmos メニューでのコンポーネント別 ON/OFF にも従う。
        /// どちらも SDK のギズモが従う条件で、代替パスも同じにする。
        /// </summary>
        private static bool IsDrawable(VRCPhysBoneBase pb)
        {
            return pb != null
                   && pb.isActiveAndEnabled
                   && pb.showGizmos
                   && GizmoAnnotationGate.IsGizmoEnabled(pb.GetType());
        }

        // ---------------------------------------------------------------
        // 組み立てと描画
        // ---------------------------------------------------------------

        private static void Rebuild()
        {
            var stopwatch = Stopwatch.StartNew();

            Canvas.Clear();
            IReadOnlyList<IPhysBoneGizmoExtension> extensions = PhysBoneGizmoPass.Extensions;

            for (int i = 0; i < Targets.Count; i++)
            {
                Target target = Targets[i];
                if (target.physBone == null) continue;

                Canvas.BeginComponent(target.physBone, target.selected);

                for (int e = 0; e < extensions.Count; e++)
                {
                    try
                    {
                        extensions[e].Build(target.physBone, Canvas);
                    }
                    catch (System.Exception ex)
                    {
                        // 拡張の失敗でパス全体（と GUI の状態）を巻き込まない。
                        UnityEngine.Debug.LogException(ex);
                    }
                }

                if (!Canvas.SuppressDefault)
                    PhysBoneGizmoShapes.Build(target.physBone, Canvas);
            }

            LastVertexCount = Canvas.VertexCount;
            LastBuildMs = stopwatch.Elapsed.TotalMilliseconds;
        }

        private static void Draw()
        {
            if (Canvas.VertexCount == 0) return;

            // 即時描画。SetPass は 1 回だけ。
            HandlesMaterial.Apply();
            Canvas.Triangles.Draw();
            Canvas.Lines.Draw();
        }
    }
}
#endif
