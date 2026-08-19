// VRChat SDK（com.vrchat.base）がある環境でだけコンパイルされる。
// 判定は asmdef の versionDefines に任せてあるので、手動設定は要らない。
#if YOZOLAB_PBRADIUS_VRCSDK
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace YozoLab.PBRadiusGizmo
{
    /// <summary>
    /// PhysBone を選んでいる間、Collision Radius をシーン上で掴んで変えられるようにする。
    ///
    /// やっていること。
    ///   ・選択中の PhysBone の半径を、SDK と同じ形（先細りカプセル／球）で重ねて描く。
    ///   ・その先頭にスケールハンドル（軸＋立方体）を出し、引っぱると Radius が変わる。
    ///   ・掴んでいる間だけ、その PhysBone のギズモ（SDK 本来のもの、または
    ///     VRC Gizmo Accelerator の代替形状）を消す。重なって値が読めなくなるため。
    ///
    /// 書き戻しは SerializedObject 経由。Undo もプレハブのオーバーライドも
    /// インスペクタで打ち込んだときと同じ扱いになる。
    ///
    /// メニュー <c>YozoLab/PhysBone/Collision Radius をシーンで操作</c> で切り替え可能。既定は ON。
    /// VRC Gizmo Accelerator が有効なときは、その代替パスと連携する
    /// （<see cref="RadiusGizmoPassBridge"/>）。
    /// </summary>
    [InitializeOnLoad]
    internal static class PhysBoneRadiusGizmo
    {
        private const string MenuPath = "YozoLab/PhysBone/Collision Radius をシーンで操作";
        private const string Pref = "YozoLab.PBRadiusGizmo.Enabled";

        /// <summary>Ctrl（macOS は Command）を押しながらのときの丸め幅。</summary>
        private const float SnapStep = 0.001f;

        /// <summary>
        /// 1 コンポーネントあたりに描く図形の上限。
        /// 数百本のチェーンで線を出し切ると、掴む前の段階で重くなって本末転倒になる。
        /// </summary>
        private const int MaxDrawnSegments = 512;

        private static readonly Color IdleColor = new Color(0.35f, 0.85f, 1f, 0.55f);
        private static readonly Color ActiveColor = new Color(1f, 0.78f, 0.25f, 0.95f);

        private static readonly int HandleHash = "YozoLab.PBRadiusGizmo".GetHashCode();

        private static readonly List<VRCPhysBoneBase> Components = new List<VRCPhysBoneBase>();
        private static readonly List<PhysBoneRadiusChain.Segment> Segments =
            new List<PhysBoneRadiusChain.Segment>();

        /// <summary>Layout / Repaint で測り直し、マウス操作の間はこれを見る。</summary>
        private static readonly Dictionary<int, Anchor> Anchors = new Dictionary<int, Anchor>();

        private static GUIStyle _labelStyle;

        // ドラッグ中だけ意味を持つ状態。
        private static VRCPhysBoneBase _dragTarget;
        private static Vector3 _dragCenter;
        private static Vector3 _dragDirection;
        private static Vector2 _dragStartMouse;
        private static Vector2 _dragCurrentMouse;
        private static float _dragStartWorldRadius;
        private static float _dragStartRadius;
        private static float _dragFactor;
        private static int _undoGroup;

        /// <summary>ハンドルを置く場所と、そこでの半径の換算。</summary>
        private struct Anchor
        {
            public bool valid;
            public Vector3 center;
            public Quaternion rotation;
            public float worldRadius;

            /// <summary>Radius 1 あたりの世界座標での半径（カーブ × スケール）。</summary>
            public float factor;
        }

        /// <summary>機能の有効/無効。</summary>
        public static bool Enabled { get; private set; }

        static PhysBoneRadiusGizmo()
        {
            Enabled = EditorPrefs.GetBool(Pref, true);

            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += Anchors.Clear;

            EditorApplication.delayCall += () => Menu.SetChecked(MenuPath, Enabled);
        }

        // ---------------------------------------------------------------
        // メニュー（トグル）
        // ---------------------------------------------------------------

        [MenuItem(MenuPath, false, 301)]
        private static void Toggle() => SetEnabled(!Enabled);

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        public static void SetEnabled(bool value)
        {
            Enabled = value;
            EditorPrefs.SetBool(Pref, Enabled);
            Menu.SetChecked(MenuPath, Enabled);

            if (!Enabled) CancelDrag();

            Anchors.Clear();
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------
        // シーン上の描画と操作
        // ---------------------------------------------------------------

        private static void OnSceneGUI(SceneView view)
        {
            if (!Enabled) return;

            GameObject active = Selection.activeGameObject;
            if (active == null) return;

            active.GetComponents(Components);
            for (int i = 0; i < Components.Count; i++) DrawComponent(Components[i], view);
            Components.Clear();
        }

        private static void DrawComponent(VRCPhysBoneBase pb, SceneView view)
        {
            // 制御 ID はイベントの種類によらず同じ順で取る（Unity の作法）。
            int id = GUIUtility.GetControlID(HandleHash, FocusType.Passive);

            if (pb == null || !pb.isActiveAndEnabled) return;

            // ギズモを切っている PhysBone には出さない。SDK の表示と足並みを揃える。
            if (!pb.showGizmos) return;

            Event e = Event.current;
            EventType type = e.GetTypeForControl(id);
            bool hot = GUIUtility.hotControl == id;
            int key = pb.GetInstanceID();

            if (type == EventType.Layout || type == EventType.Repaint)
            {
                PhysBoneRadiusChain.Build(pb, Segments);
                Anchors[key] = MeasureAnchor(pb, Segments);
            }

            if (!Anchors.TryGetValue(key, out Anchor anchor)) return;

            if (type == EventType.Repaint) DrawSegments(Segments, hot);

            if (!anchor.valid) return;

            Vector3 direction = hot
                ? _dragDirection
                : RadiusHandleMath.PickHandleDirection(
                    anchor.rotation * Vector3.right,
                    anchor.rotation * Vector3.forward,
                    ViewForward(view),
                    ViewRight(view));

            Vector3 handlePosition = anchor.center + direction * anchor.worldRadius;
            float capSize = HandleUtility.GetHandleSize(handlePosition) * 0.08f;

            switch (type)
            {
                case EventType.Layout:
                case EventType.MouseMove:
                    HandleUtility.AddControl(id, HandleUtility.DistanceToCircle(handlePosition, capSize));
                    break;

                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && HandleUtility.nearestControl == id)
                    {
                        BeginDrag(id, pb, anchor, direction, e.mousePosition);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (hot)
                    {
                        Drag(pb, e);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (hot)
                    {
                        EndDrag();
                        e.Use();
                    }
                    break;

                case EventType.KeyDown:
                    // 掴んだまま Esc で、掴む前の値へ戻す。
                    if (hot && e.keyCode == KeyCode.Escape)
                    {
                        ApplyRadius(pb, _dragStartRadius);
                        EndDrag();
                        e.Use();
                    }
                    break;

                case EventType.Repaint:
                    DrawHandle(id, pb, anchor, handlePosition, capSize, hot, view);
                    break;
            }
        }

        /// <summary>組み立てた図形から、ハンドルを置く場所を選ぶ。</summary>
        private static Anchor MeasureAnchor(VRCPhysBoneBase pb, List<PhysBoneRadiusChain.Segment> segments)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                PhysBoneRadiusChain.Segment segment = segments[i];
                if (segment.handleFactor <= 0f) continue;

                return new Anchor
                {
                    valid = true,
                    center = segment.HandleCenter,
                    rotation = segment.rotation,
                    worldRadius = segment.HandleRadius,
                    factor = segment.handleFactor,
                };
            }

            // Radius が 0 のときは図形が 1 つも出ない。0 から増やせるよう、
            // 先頭のボーンを基準にしたハンドルだけ出す。
            if (PhysBoneRadiusChain.TryGetZeroRadiusAnchor(
                    pb, out Vector3 center, out Quaternion rotation, out float factor))
            {
                return new Anchor
                {
                    valid = true,
                    center = center,
                    rotation = rotation,
                    worldRadius = pb.radius * factor,
                    factor = factor,
                };
            }

            return default;
        }

        // ---------------------------------------------------------------
        // ドラッグ
        // ---------------------------------------------------------------

        private static void BeginDrag(
            int id, VRCPhysBoneBase pb, Anchor anchor, Vector3 direction, Vector2 mousePosition)
        {
            GUIUtility.hotControl = id;
            EditorGUIUtility.SetWantsMouseJumping(1);

            _dragTarget = pb;
            _dragCenter = anchor.center;
            _dragDirection = direction;
            _dragStartMouse = mousePosition;
            _dragCurrentMouse = mousePosition;
            _dragStartWorldRadius = anchor.worldRadius;
            _dragStartRadius = pb.radius;
            _dragFactor = anchor.factor;

            // ドラッグ中の書き込みは何度も起きる。1 回の操作として畳むため、
            // 掴んだ時点のグループ番号を覚えておく。
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();

            MuteGizmos(pb);
        }

        private static void Drag(VRCPhysBoneBase pb, Event e)
        {
            // 掴んでいる間はマウスが画面端で折り返る（SetWantsMouseJumping）ので、
            // mousePosition ではなく delta を足し込む。Unity 本体のハンドルと同じ作法。
            _dragCurrentMouse += e.delta;

            float translation = HandleUtility.CalcLineTranslation(
                _dragStartMouse, _dragCurrentMouse, _dragCenter, _dragDirection);

            float world = Mathf.Max(0f, _dragStartWorldRadius + translation);
            float value = RadiusHandleMath.BaseRadiusFromWorld(world, _dragFactor);

            if (e.control || e.command) value = RadiusHandleMath.Snap(value, SnapStep);

            ApplyRadius(pb, value);
        }

        private static void EndDrag()
        {
            GUIUtility.hotControl = 0;
            EditorGUIUtility.SetWantsMouseJumping(0);

            Undo.CollapseUndoOperations(_undoGroup);
            Undo.SetCurrentGroupName("Change PhysBone Collision Radius");

            _dragTarget = null;
            UnmuteGizmos();
        }

        /// <summary>掴んだままの状態を、値に触れずに畳む（機能を切ったとき用）。</summary>
        private static void CancelDrag()
        {
            if (_dragTarget == null) return;

            GUIUtility.hotControl = 0;
            EditorGUIUtility.SetWantsMouseJumping(0);
            _dragTarget = null;
            UnmuteGizmos();
        }

        /// <summary>ドラッグ中の PhysBone か（Accelerator の代替パスが問い合わせる）。</summary>
        internal static bool IsDragging(Component physBone) =>
            _dragTarget != null && ReferenceEquals(physBone, _dragTarget);

        /// <summary>
        /// 掴んでいる 1 体のギズモを消す。Accelerator の代替パスが動いていれば
        /// そちらに組み立て直させ（ブリッジの拡張が既定形状を伏せる）、
        /// 動いていなければ SDK の showGizmos を直接伏せる。
        /// </summary>
        private static void MuteGizmos(VRCPhysBoneBase pb)
        {
#if YOZOLAB_HAS_VRCGIZMOACC
            if (RadiusGizmoPassBridge.PassActive)
            {
                RadiusGizmoPassBridge.InvalidatePass();
                return;
            }
#endif
            SdkGizmoMuter.Begin(pb);
        }

        private static void UnmuteGizmos()
        {
            SdkGizmoMuter.End();
#if YOZOLAB_HAS_VRCGIZMOACC
            if (RadiusGizmoPassBridge.PassActive) RadiusGizmoPassBridge.InvalidatePass();
#endif
        }

        /// <summary>
        /// Radius を書き戻す。
        ///
        /// 直接フィールドへ入れずに SerializedObject を通すのは、Undo と
        /// プレハブのオーバーライド（太字の表示、Revert の対象）を
        /// インスペクタでの編集と揃えるため。
        /// </summary>
        private static void ApplyRadius(VRCPhysBoneBase pb, float value)
        {
            var serialized = new SerializedObject(pb);
            SerializedProperty property = serialized.FindProperty("radius");
            if (property == null) return;
            if (property.floatValue == value) return;

            property.floatValue = value;
            serialized.ApplyModifiedProperties();

            // 再生中は、設定を読み直させないと当たり判定に反映されない。
            if (Application.isPlaying) pb.configHasUpdated = true;
        }

        // ---------------------------------------------------------------
        // 描画
        // ---------------------------------------------------------------

        private static void DrawSegments(List<PhysBoneRadiusChain.Segment> segments, bool active)
        {
            using (new Handles.DrawingScope(active ? ActiveColor : IdleColor))
            {
                int count = Mathf.Min(segments.Count, MaxDrawnSegments);
                for (int i = 0; i < count; i++)
                {
                    PhysBoneRadiusChain.Segment segment = segments[i];
                    Vector3 up = segment.rotation * Vector3.up;
                    Vector3 right = segment.rotation * Vector3.right;
                    Vector3 forward = segment.rotation * Vector3.forward;

                    if (segment.isSphere)
                    {
                        DrawWireSphere(segment.end, segment.endRadius, up, right, forward);
                        continue;
                    }

                    // 先細りカプセル。両端の輪と、それを繋ぐ 4 本で形が読める。
                    Handles.DrawWireDisc(segment.start, up, segment.startRadius);
                    Handles.DrawWireDisc(segment.end, up, segment.endRadius);

                    DrawTaperLine(segment, right);
                    DrawTaperLine(segment, -right);
                    DrawTaperLine(segment, forward);
                    DrawTaperLine(segment, -forward);
                }
            }
        }

        private static void DrawTaperLine(PhysBoneRadiusChain.Segment segment, Vector3 direction)
        {
            Handles.DrawLine(
                segment.start + direction * segment.startRadius,
                segment.end + direction * segment.endRadius);
        }

        private static void DrawWireSphere(
            Vector3 center, float radius, Vector3 up, Vector3 right, Vector3 forward)
        {
            Handles.DrawWireDisc(center, up, radius);
            Handles.DrawWireDisc(center, right, radius);
            Handles.DrawWireDisc(center, forward, radius);
        }

        private static void DrawHandle(
            int id, VRCPhysBoneBase pb, Anchor anchor, Vector3 handlePosition,
            float capSize, bool active, SceneView view)
        {
            using (new Handles.DrawingScope(active ? ActiveColor : IdleColor))
            {
                Handles.DrawLine(anchor.center, handlePosition);
                Handles.CubeHandleCap(
                    id, handlePosition, Quaternion.identity, capSize, EventType.Repaint);

                if (!active) return;

                Vector3 offset = ViewUp(view) * capSize * 3f;
                Handles.Label(handlePosition + offset, $"Radius {pb.radius:0.####}", LabelStyle());
            }
        }

        private static GUIStyle LabelStyle()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                _labelStyle.normal.textColor = ActiveColor;
            }
            return _labelStyle;
        }

        // シーンビューのカメラが無い状況（起動直後など）でも落ちないようにしておく。
        private static Vector3 ViewForward(SceneView view) =>
            view != null && view.camera != null ? view.camera.transform.forward : Vector3.forward;

        private static Vector3 ViewRight(SceneView view) =>
            view != null && view.camera != null ? view.camera.transform.right : Vector3.right;

        private static Vector3 ViewUp(SceneView view) =>
            view != null && view.camera != null ? view.camera.transform.up : Vector3.up;
    }
}
#endif
