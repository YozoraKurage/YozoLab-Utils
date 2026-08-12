using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// ギズモの入口の出入りを管理する。
    ///
    /// prefix が false を返した（= SDK の Draw を飛ばした）場合でも Harmony の
    /// finalizer は走るので、「区間を開いたかどうか」を積んでおいて、
    /// 開いたものだけ閉じる。
    ///
    /// 併せて、1 リペイントぶんの発行をまとめる役目も持つ。ギズモのコールバックは
    /// コンポーネントごとに呼ばれ、どこがパスの終わりかは分からない。そこで
    /// 「パスの最初に、前のパスで束ねたものを 1 回で描く」形にしている。
    /// 1 フレーム遅れるが、そのぶん SetPass はリペイントに 1 回で済む。
    /// </summary>
    internal static class GizmoScopes
    {
        private static readonly List<bool> Opened = new List<bool>(8);

        // このパスで描かれたコンポーネント（順序も束ね直しに使う）
        private static readonly List<int> SeenThisPass = new List<int>(64);
        private static readonly List<int> SeenLastPass = new List<int>(64);
        private static readonly List<GizmoGeometryCache.Entry> Ordered =
            new List<GizmoGeometryCache.Entry>(64);

        private static int _frame = -1;
        private static int _camera;

        /// <summary>
        /// 区間に入る。戻り値は「元の実装を走らせるか」。
        /// キャッシュがそのまま使えるときだけ false になる。
        /// </summary>
        internal static bool Enter(Object target)
        {
            var settings = VRCGizmoAcceleratorSettings.instance;

            if (target == null || !settings.cacheGeometry || !GizmoBatch.IsDrawingEventNow)
            {
                GizmoBatch.Begin();
                Opened.Add(true);
                return true;
            }

            var component = target as Component;
            if (component == null)
            {
                GizmoBatch.Begin();
                Opened.Add(true);
                return true;
            }

            int id = component.GetInstanceID();

            if (settings.combineDrawCalls) BeginPassIfNeeded(id);

            SeenThisPass.Add(id);

            // ボーンが動いていないか毎フレーム確かめる。変更イベントを出さずに
            // Transform が動く経路（他ツールのプレビュー等）があるので、
            // 時間で諦める作りだとギズモが遅れて見える。
            int fingerprint = TransformFingerprint.Compute(
                component.transform, RootTransformOf(component));

            // カメラ依存のもの（コライダー等）は、カメラが動いていない間だけ使える
            int cameraFingerprint = CameraFingerprint.Compute();

            var cached = GizmoGeometryCache.Find(id, fingerprint, cameraFingerprint);
            if (cached != null)
            {
                GizmoGeometryCache.CountHit();

                // 束ねる設定なら、このパスの頭で既に描いてある。
                if (!settings.combineDrawCalls) GizmoBatch.DrawCached(cached);

                Opened.Add(false);
                return false;
            }

            GizmoBatch.Begin(GizmoGeometryCache.Prepare(id), fingerprint, cameraFingerprint,
                suppressDraw: settings.combineDrawCalls);
            Opened.Add(true);
            return true;
        }

        /// <summary>
        /// パスの先頭でだけ走る。前のパスで束ねたものを 1 回で描き、
        /// 顔ぶれや中身が変わっていれば束ね直す。
        /// </summary>
        private static void BeginPassIfNeeded(int incomingId)
        {
            // シーンビューが複数あると同じフレームで複数回描かれるので、
            // フレームだけでなく描画中のカメラも見る。
            int frame = Time.renderedFrameCount;
            var camera = Camera.current;
            int cameraId = camera != null ? camera.GetInstanceID() : 0;

            // フレームもカメラも変わらないのに、同じ相手がもう一度来た場合も
            // パスの切り替わりとみなす（フレーム番号が進まない環境への保険）。
            bool restarted = SeenThisPass.Contains(incomingId);

            if (frame == _frame && cameraId == _camera && !restarted) return;

            _frame = frame;
            _camera = cameraId;

            bool sameLineup = SameAsLastPass();

            SeenLastPass.Clear();
            SeenLastPass.AddRange(SeenThisPass);
            SeenThisPass.Clear();

            Ordered.Clear();
            foreach (int id in SeenLastPass)
            {
                var entry = GizmoGeometryCache.Peek(id);
                if (entry != null) Ordered.Add(entry);
            }

            if (!sameLineup) CombinedGizmoMesh.Invalidate();
            if (CombinedGizmoMesh.NeedsRebuild) CombinedGizmoMesh.Rebuild(Ordered);
            CombinedGizmoMesh.Draw();
        }

        private static bool SameAsLastPass()
        {
            if (SeenThisPass.Count != SeenLastPass.Count) return false;
            for (int i = 0; i < SeenThisPass.Count; i++)
            {
                if (SeenThisPass[i] != SeenLastPass[i]) return false;
            }
            return true;
        }

        internal static void Exit()
        {
            if (Opened.Count == 0) return;

            bool opened = Opened[Opened.Count - 1];
            Opened.RemoveAt(Opened.Count - 1);

            if (opened) GizmoBatch.End();
        }

        internal static void Reset()
        {
            Opened.Clear();
            SeenThisPass.Clear();
            SeenLastPass.Clear();
            Ordered.Clear();
            RootTransformFields.Clear();
            _frame = -1;
            _camera = 0;
        }

        // 型ごとの rootTransform フィールド（PhysBone は別階層をルートにできる）
        private static readonly Dictionary<System.Type, System.Reflection.FieldInfo> RootTransformFields =
            new Dictionary<System.Type, System.Reflection.FieldInfo>();

        /// <summary>
        /// rootTransform が指定されていれば、そちらの階層も指紋に含める。
        /// 自分の下に無いボーンを動かしたときに気付けなくなるのを防ぐ。
        /// </summary>
        private static Transform RootTransformOf(Component component)
        {
            var type = component.GetType();
            if (!RootTransformFields.TryGetValue(type, out var field))
            {
                field = type.GetField("rootTransform",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.FlattenHierarchy);

                if (field != null && field.FieldType != typeof(Transform)) field = null;
                RootTransformFields[type] = field;
            }

            return field?.GetValue(component) as Transform;
        }
    }
}
