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
    /// PhysBone の Collision Radius を、ボーンごとのハンドルでシーン上から変える。
    ///
    /// 見せ方は「ボーン 1 本ずつが自分の Radius を持っている」ように振る舞うが、
    /// 実体はそうなっていない。半径は radius × radiusCurve(チェーン位置) ×
    /// スケールで決まるので、裏では掴んだボーンの位置にあるカーブのキーを
    /// 編集する。掴んだ瞬間に全ボーンの位置へ現在値のキーを打って各ボーンを
    /// 固定するため、動くのは掴んだボーンだけになる（radius 本体には触れない。
    /// Radius が 0 のときだけ、0 には何を掛けても増やせないので本体を動かす）。
    ///
    /// ハンドルはインスペクタで ON にした PhysBone にだけ出る
    /// （<see cref="RadiusHandleToggle"/>）。出ているだけで毎フレームの組み立てと
    /// 当たり判定を伴うので、既定は OFF。
    ///
    /// 書き戻しは SerializedObject 経由。Undo もプレハブのオーバーライドも
    /// インスペクタで打ち込んだときと同じ扱いになる。
    ///
    /// ドラッグ中はその PhysBone のギズモ（SDK 本来のもの、または
    /// VRC Gizmo Accelerator の代替形状）を消す。重なって値が読めなくなるため。
    ///
    /// メニュー <c>YozoLab/PhysBone/Collision Radius をシーンで操作</c> で切り替え可能。既定は ON。
    /// </summary>
    [InitializeOnLoad]
    internal static class PhysBoneRadiusGizmo
    {
        private const string MenuPath = "YozoLab/PhysBone/Collision Radius をシーンで操作";
        private const string Pref = "YozoLab.PBRadiusGizmo.Enabled";

        /// <summary>Ctrl（macOS は Command）を押しながらのときの、ボーン半径の丸め幅。</summary>
        private const float SnapStep = 0.001f;

        /// <summary>
        /// 1 コンポーネントあたりのハンドル数の上限。
        /// 数百本のチェーンで全部に出すと、掴む前の段階で重くなって本末転倒になる。
        /// </summary>
        private const int MaxHandles = 256;

        private static readonly Color IdleColor = new Color(0.35f, 0.85f, 1f, 0.55f);
        private static readonly Color ActiveColor = new Color(1f, 0.78f, 0.25f, 0.95f);

        private static readonly int HandleHash = "YozoLab.PBRadiusGizmo".GetHashCode();

        private static readonly List<VRCPhysBoneBase> Components = new List<VRCPhysBoneBase>();
        private static readonly List<PhysBoneRadiusChain.Segment> Segments =
            new List<PhysBoneRadiusChain.Segment>();
        private static readonly List<float> RatioBuffer = new List<float>();

        /// <summary>ハンドル 1 つ分。Layout / Repaint で測り直す。</summary>
        private struct Anchor
        {
            public Vector3 center;
            public Quaternion rotation;
            public float worldRadius;

            /// <summary>カーブのキーを打つチェーン位置（isBase のときは未使用）。</summary>
            public float ratio;

            /// <summary>この点のスケール。世界半径とカーブ値の換算に使う。</summary>
            public float scale;

            /// <summary>true なら radius 本体を動かす（Radius が 0 のとき）。</summary>
            public bool isBase;

            /// <summary>isBase のとき、Radius 1 あたりの世界半径。</summary>
            public float baseFactor;
        }

        private static readonly Dictionary<int, List<Anchor>> Anchors =
            new Dictionary<int, List<Anchor>>();

        private static GUIStyle _labelStyle;

        // ---- ドラッグ中だけ意味を持つ状態 ------------------------------------

        private enum DragMode { None, BaseRadius, CurveKey }

        private static DragMode _dragMode;
        private static VRCPhysBoneBase _dragTarget;
        private static int _dragId;
        private static Vector3 _dragCenter;
        private static Vector3 _dragDirection;
        private static Vector2 _dragStartMouse;
        private static Vector2 _dragCurrentMouse;
        private static float _dragStartWorldRadius;
        private static float _dragRatio;
        private static float _dragScale;

        // BaseRadius モード用
        private static float _dragStartRadius;
        private static float _dragBaseFactor;

        // CurveKey モード用
        private static AnimationCurve _dragOriginalCurve;   // Esc で戻す先
        private static AnimationCurve _dragCurveBase;       // 全ボーンにキーを打った土台
        private static int _dragKeyIndex;

        private static int _undoGroup;

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

        /// <summary>ドラッグ中の PhysBone か（Accelerator の代替パスが問い合わせる）。</summary>
        internal static bool IsDragging(Component physBone) =>
            _dragMode != DragMode.None && ReferenceEquals(physBone, _dragTarget);

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
            if (pb == null || !pb.isActiveAndEnabled) return;

            // ギズモを切っている PhysBone には出さない。SDK の表示と足並みを揃える。
            if (!pb.showGizmos) return;

            bool draggingThis = _dragMode != DragMode.None && ReferenceEquals(_dragTarget, pb);

            // インスペクタ側のトグルで選ばれた PhysBone だけに出す。
            // （ドラッグ中に OFF へ切られても、その操作だけは最後まで面倒を見る）
            if (!RadiusHandleToggle.IsEnabled(pb) && !draggingThis) return;

            Event e = Event.current;
            int key = pb.GetInstanceID();

            if (e.type == EventType.Layout || e.type == EventType.Repaint)
            {
                PhysBoneRadiusChain.Build(pb, Segments);
                MeasureAnchors(pb, key);
            }

            if (!Anchors.TryGetValue(key, out List<Anchor> anchors)) return;

            if (e.type == EventType.Repaint) DrawSegments(Segments, draggingThis);

            if (draggingThis)
            {
                HandleDragEvents(pb, e, view);
                return;
            }

            // 別の PhysBone を掴んでいる間は、こちらのハンドルは黙る。
            if (_dragMode != DragMode.None) return;

            for (int i = 0; i < anchors.Count; i++)
                HandleIdleAnchor(pb, anchors[i], e, view);
        }

        /// <summary>ハンドルを置く場所を測る。ボーンごとに 1 つ、Radius 0 なら先頭に 1 つ。</summary>
        private static void MeasureAnchors(VRCPhysBoneBase pb, int key)
        {
            if (!Anchors.TryGetValue(key, out List<Anchor> anchors))
            {
                anchors = new List<Anchor>();
                Anchors[key] = anchors;
            }
            anchors.Clear();

            if (pb.radius > 0f)
            {
                int count = Mathf.Min(Segments.Count, MaxHandles);
                for (int i = 0; i < count; i++)
                {
                    PhysBoneRadiusChain.Segment segment = Segments[i];
                    if (segment.HandleScale <= 0f) continue;

                    anchors.Add(new Anchor
                    {
                        center = segment.HandleCenter,
                        rotation = segment.rotation,
                        worldRadius = segment.HandleRadius,
                        ratio = segment.HandleRatio,
                        scale = segment.HandleScale,
                        isBase = false,
                    });
                }
                return;
            }

            // Radius が 0 のときは図形が 1 つも出ない。カーブに何を掛けても 0 の
            // ままなので、ここだけは radius 本体を動かすハンドルを先頭に 1 つ出す。
            if (PhysBoneRadiusChain.TryGetZeroRadiusAnchor(
                    pb, out Vector3 center, out Quaternion rotation, out float factor))
            {
                anchors.Add(new Anchor
                {
                    center = center,
                    rotation = rotation,
                    worldRadius = 0f,
                    isBase = true,
                    baseFactor = factor,
                });
            }
        }

        // ---------------------------------------------------------------
        // 待機中のハンドル
        // ---------------------------------------------------------------

        private static void HandleIdleAnchor(VRCPhysBoneBase pb, Anchor anchor, Event e, SceneView view)
        {
            // 制御 ID はイベントの種類によらず同じ順で取る（Unity の作法）。
            int id = GUIUtility.GetControlID(HandleHash, FocusType.Passive);

            Vector3 direction = RadiusHandleMath.PickHandleDirection(
                anchor.rotation * Vector3.right,
                anchor.rotation * Vector3.forward,
                ViewForward(view),
                ViewRight(view));

            Vector3 handlePosition = anchor.center + direction * anchor.worldRadius;
            float capSize = HandleUtility.GetHandleSize(handlePosition) * 0.08f;

            switch (e.GetTypeForControl(id))
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

                case EventType.Repaint:
                    using (new Handles.DrawingScope(IdleColor))
                    {
                        Handles.DrawLine(anchor.center, handlePosition);
                        Handles.CubeHandleCap(
                            id, handlePosition, Quaternion.identity, capSize, EventType.Repaint);
                    }
                    break;
            }
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
            _dragId = id;
            _dragCenter = anchor.center;
            _dragDirection = direction;
            _dragStartMouse = mousePosition;
            _dragCurrentMouse = mousePosition;
            _dragStartWorldRadius = anchor.worldRadius;
            _dragRatio = anchor.ratio;
            _dragScale = anchor.scale;

            // ドラッグ中の書き込みは何度も起きる。1 回の操作として畳むため、
            // 掴んだ時点のグループ番号を覚えておく。
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();

            if (anchor.isBase)
            {
                _dragMode = DragMode.BaseRadius;
                _dragStartRadius = pb.radius;
                _dragBaseFactor = anchor.baseFactor;
            }
            else
            {
                _dragMode = DragMode.CurveKey;
                BeginCurveDrag(pb, anchor.ratio);
            }

            MuteGizmos(pb);
        }

        /// <summary>
        /// カーブ編集の下ごしらえ。全ボーンの位置に現在値のキーを打ち、
        /// 掴んだ位置のキーを覚える。以後のドラッグはそのキーの値だけ動かす。
        /// </summary>
        private static void BeginCurveDrag(VRCPhysBoneBase pb, float ratio)
        {
            var serialized = new SerializedObject(pb);
            SerializedProperty property = serialized.FindProperty("radiusCurve");
            _dragOriginalCurve = property?.animationCurveValue;

            RatioBuffer.Clear();
            for (int i = 0; i < Segments.Count; i++)
            {
                RatioBuffer.Add(Segments[i].startRatio);
                RatioBuffer.Add(Segments[i].endRatio);
            }

            float radius = pb.radius;
            _dragCurveBase = RadiusHandleMath.BuildPerBoneKeys(
                RatioBuffer, t => pb.CalcRadius(t) / radius);
            _dragKeyIndex = RadiusHandleMath.FindKeyIndex(_dragCurveBase, ratio);

            // キーを打っただけの状態を書き込んでおく（値はどこも変わらない）。
            ApplyCurve(pb, _dragCurveBase);
        }

        private static void HandleDragEvents(VRCPhysBoneBase pb, Event e, SceneView view)
        {
            switch (e.GetTypeForControl(_dragId))
            {
                case EventType.MouseDrag:
                    Drag(pb, e);
                    e.Use();
                    break;

                case EventType.MouseUp:
                    EndDrag();
                    e.Use();
                    break;

                case EventType.KeyDown:
                    // 掴んだまま Esc で、掴む前の状態へ戻す。
                    if (e.keyCode == KeyCode.Escape)
                    {
                        RevertDrag(pb);
                        EndDrag();
                        e.Use();
                    }
                    break;

                case EventType.Repaint:
                    DrawDragHandle(pb, view);
                    break;
            }
        }

        private static void Drag(VRCPhysBoneBase pb, Event e)
        {
            // 掴んでいる間はマウスが画面端で折り返る（SetWantsMouseJumping）ので、
            // mousePosition ではなく delta を足し込む。Unity 本体のハンドルと同じ作法。
            _dragCurrentMouse += e.delta;

            float translation = HandleUtility.CalcLineTranslation(
                _dragStartMouse, _dragCurrentMouse, _dragCenter, _dragDirection);
            float world = Mathf.Max(0f, _dragStartWorldRadius + translation);

            if (_dragMode == DragMode.BaseRadius)
            {
                float value = RadiusHandleMath.BaseRadiusFromWorld(world, _dragBaseFactor);
                if (e.control || e.command) value = RadiusHandleMath.Snap(value, SnapStep);
                ApplyRadius(pb, value);
                return;
            }

            if (_dragKeyIndex < 0) return;

            // 世界半径 → カーブ値。丸めは「そのボーンの半径」（radius × カーブ値）に掛ける。
            float radius = pb.radius;
            float curveValue = RadiusHandleMath.BaseRadiusFromWorld(world, radius * _dragScale);
            if (e.control || e.command)
                curveValue = RadiusHandleMath.Snap(curveValue * radius, SnapStep) / radius;

            ApplyCurve(pb, RadiusHandleMath.WithKeyValue(_dragCurveBase, _dragKeyIndex, curveValue));
        }

        private static void RevertDrag(VRCPhysBoneBase pb)
        {
            if (_dragMode == DragMode.BaseRadius)
            {
                ApplyRadius(pb, _dragStartRadius);
            }
            else if (_dragOriginalCurve != null)
            {
                ApplyCurve(pb, _dragOriginalCurve);
            }
        }

        private static void EndDrag()
        {
            GUIUtility.hotControl = 0;
            EditorGUIUtility.SetWantsMouseJumping(0);

            Undo.CollapseUndoOperations(_undoGroup);
            Undo.SetCurrentGroupName("Change PhysBone Collision Radius");

            ClearDragState();
        }

        /// <summary>掴んだままの状態を、値に触れずに畳む（機能を切ったとき用）。</summary>
        private static void CancelDrag()
        {
            if (_dragMode == DragMode.None) return;

            GUIUtility.hotControl = 0;
            EditorGUIUtility.SetWantsMouseJumping(0);
            ClearDragState();
        }

        private static void ClearDragState()
        {
            _dragMode = DragMode.None;
            _dragTarget = null;
            _dragOriginalCurve = null;
            _dragCurveBase = null;
            UnmuteGizmos();
        }

        // ---------------------------------------------------------------
        // 書き戻し
        // ---------------------------------------------------------------

        /// <summary>
        /// SerializedObject を通すのは、Undo とプレハブのオーバーライド
        /// （太字の表示、Revert の対象）をインスペクタでの編集と揃えるため。
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

        private static void ApplyCurve(VRCPhysBoneBase pb, AnimationCurve curve)
        {
            var serialized = new SerializedObject(pb);
            SerializedProperty property = serialized.FindProperty("radiusCurve");
            if (property == null) return;

            property.animationCurveValue = curve;
            serialized.ApplyModifiedProperties();

            if (Application.isPlaying) pb.configHasUpdated = true;
        }

        // ---------------------------------------------------------------
        // 描画
        // ---------------------------------------------------------------

        private static void DrawSegments(List<PhysBoneRadiusChain.Segment> segments, bool active)
        {
            using (new Handles.DrawingScope(active ? ActiveColor : IdleColor))
            {
                int count = Mathf.Min(segments.Count, MaxHandles);
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

        private static void DrawDragHandle(VRCPhysBoneBase pb, SceneView view)
        {
            // 今の値での位置。ボーンはドラッグ中に動かないので、中心と向きは掴んだときのまま。
            float world;
            float shownRadius;
            if (_dragMode == DragMode.BaseRadius)
            {
                world = pb.radius * _dragBaseFactor;
                shownRadius = pb.radius;
            }
            else
            {
                world = pb.CalcRadius(_dragRatio) * _dragScale;
                shownRadius = pb.CalcRadius(_dragRatio);
            }

            Vector3 handlePosition = _dragCenter + _dragDirection * world;
            float capSize = HandleUtility.GetHandleSize(handlePosition) * 0.08f;

            using (new Handles.DrawingScope(ActiveColor))
            {
                Handles.DrawLine(_dragCenter, handlePosition);
                Handles.CubeHandleCap(
                    _dragId, handlePosition, Quaternion.identity, capSize, EventType.Repaint);

                Vector3 offset = ViewUp(view) * capSize * 3f;
                Handles.Label(handlePosition + offset, $"Radius {shownRadius:0.####}", LabelStyle());
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

        // ---------------------------------------------------------------
        // ギズモの黙らせ方
        // ---------------------------------------------------------------

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
