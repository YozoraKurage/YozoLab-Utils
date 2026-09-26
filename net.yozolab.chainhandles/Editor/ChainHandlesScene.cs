using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// 保存してある鎖（<see cref="ChainStore"/>）のハンドルをシーンに出し、操作を受ける。
    /// ウィンドウを開いていなくても動く。表示の ON/OFF はシーンビューのツールバー
    /// （<see cref="ChainHandlesOverlay"/>）、メニュー、ウィンドウのどこからでも切り替えられる。
    ///
    /// ハンドルは関節の上に置き、分割数（ボーンの本数まで）でおおよそ等間隔に選ぶか、
    /// 複数選択から作った場合は選んだボーンに置く。
    ///
    /// 動かし方は 2 通り。
    /// - IK（既定）: 掴んだハンドルの関節がその位置へ届くよう、根元からそこまでの
    ///   関節が全部曲がる。先の部分は形を保って運ばれる（<see cref="ChainIK"/>）。
    /// - 曲線: ハンドルを通る曲線に沿って鎖が曲がる。他のハンドルは留まる
    ///   （<see cref="ChainSolver"/>）。
    /// 回転ツール（E）のときは、選んだハンドルの関節だけを回す（FK）。
    ///
    /// 書き込むのは各関節の回転だけ。localPosition と localScale には触れないので、
    /// どう動かしてもボーンの長さとスケールは変わらない。
    ///
    /// ハンドルは掴んでいる間だけ自由に動き、離すと今の鎖の上へ置き直される。
    /// 状態を鎖の外に持たないので、Undo やアニメーションの時間移動、手で回した
    /// 後でも、ハンドルは常に見えている鎖の上から始まる。
    /// </summary>
    [InitializeOnLoad]
    internal static class ChainHandlesScene
    {
        internal const string UndoName = "Chain Handles";
        internal const int MaxDivisions = 32;

        private const string MenuPath = "YozoLab/Chain Handles/ギズモを表示";

        private static readonly Color ChainColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color CurveColor = new Color(0.35f, 0.85f, 1f, 0.8f);
        private static readonly Color HandleColor = new Color(0.35f, 0.85f, 1f, 0.9f);
        private static readonly Color RootColor = new Color(0.6f, 0.6f, 0.6f, 0.9f);
        private static readonly Color SelectedColor = new Color(1f, 0.78f, 0.25f, 1f);
        private static readonly Color RollColor = new Color(1f, 0.45f, 0.75f, 0.9f);

        private static readonly int HandleHash = "YozoLab.ChainHandles".GetHashCode();

        // ---- 選択中のハンドル --------------------------------------------
        private static string _selectedChain;
        private static int _selectedControl = -1;

        /// <summary>Unity 本体の移動・回転ギズモを隠しているか（こちらのハンドルと重なるため）。</summary>
        private static bool _toolsHidden;

        // ---- 掴んでいる間だけ意味を持つ状態 --------------------------------
        private sealed class Drag
        {
            public string chain;
            public Transform[] joints;
            public Vector3[] restPositions;
            public Quaternion[] restRotations;

            /// <summary>各ハンドルが乗っている関節の番号と、その弧長パラメータ。</summary>
            public int[] indices;
            public float[] knots;

            public Vector3[] restControls;
            public Vector3[] controls;
            public float[] rolls;

            /// <summary>解いた結果。IK ではここから続きを解く。</summary>
            public Vector3[] positions;
            public Quaternion[] rotations;

            /// <summary>IK で掴んでいるハンドルと、その行き先。</summary>
            public int active = -1;
            public Vector3 target;

            public int undoGroup;
        }

        private static Drag _drag;

        /// <summary>関節だけを回している間の Undo グループ（回していなければ -1）。</summary>
        private static int _rotateUndoGroup = -1;

        /// <summary>回転ギズモの戻り値から 1 フレーム分の回転を取り出す。</summary>
        private static readonly RotationGizmoTracker RotateTracker = new RotationGizmoTracker();

        private static readonly List<Transform> Joints = new List<Transform>();
        private static readonly List<Transform> Anchors = new List<Transform>();
        private static readonly List<int> AnchorIndices = new List<int>();
        private static readonly List<Vector3> Curve = new List<Vector3>();

        private static ChainStore Store => ChainStore.instance;

        static ChainHandlesScene()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload += () => Select(null, -1);
            EditorApplication.delayCall += () => Menu.SetChecked(MenuPath, Enabled);
        }

        // ---------------------------------------------------------------
        // 表示の ON/OFF（Utils Settings の実行時トグルからも呼ばれる）
        // ---------------------------------------------------------------

        public static bool Enabled => Store.gizmosVisible;

        public static void SetEnabled(bool value)
        {
            if (Store.gizmosVisible == value) return;
            Store.Set(() => Store.gizmosVisible = value);
            if (!value)
            {
                EndDrag();
                EndRotate();
                Select(null, -1);
            }
            Menu.SetChecked(MenuPath, value);
        }

        [MenuItem(MenuPath, false, 302)]
        private static void ToggleMenu() => SetEnabled(!Enabled);

        [MenuItem(MenuPath, true)]
        private static bool ToggleMenuValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        // ---------------------------------------------------------------
        // 鎖を作る
        // ---------------------------------------------------------------

        /// <summary>
        /// 鎖を決めてすぐ動かせる状態にする（右クリックや候補一覧から）。
        /// 始点が属するアバターの欄に足し（同じ鎖があればそれを使い）、表示を ON にして
        /// 先端のハンドルを選ぶ。
        /// </summary>
        /// <param name="anchors">ハンドルを置くボーン。null なら分割数で選ぶ。</param>
        internal static void StartChain(Transform start, Transform end, List<Transform> anchors)
        {
            Transform root = AvatarRoots.FindRoot(start);
            string startPath = AvatarRoots.PathFrom(root, start);
            string endPath = AvatarRoots.PathFrom(root, end);
            if (root == null || startPath == null || endPath == null) return;

            ChainStore.Chain chain = null;
            Store.Modify(UndoName, () =>
            {
                ChainStore.AvatarEntry avatar = Store.GetOrAdd(root);
                chain = avatar.chains.Find(c => c.start == startPath && c.end == endPath);
                if (chain == null)
                {
                    chain = new ChainStore.Chain { start = startPath, end = endPath };
                    var joints = new List<Transform>();
                    if (TransformChain.TryBuild(start, end, joints, out _))
                        chain.divisions = DefaultDivisions(joints.Count);
                    avatar.chains.Add(chain);
                }

                chain.anchors.Clear();
                if (anchors != null)
                {
                    foreach (Transform a in anchors) chain.anchors.Add(AvatarRoots.PathFrom(root, a));
                }

                chain.visible = true;
                avatar.visible = true;
                avatar.expanded = true;
                Store.gizmosVisible = true;
            });
            Menu.SetChecked(MenuPath, true);

            Select(chain.id, HandleCount(root, chain) - 1);
        }

        /// <summary>関節数に応じた初期の分割数。短い鎖で細かく割っても扱いにくいだけなので。</summary>
        internal static int DefaultDivisions(int jointCount) => Mathf.Clamp((jointCount - 1) / 2, 1, 3);

        // ---------------------------------------------------------------
        // 鎖の解決
        // ---------------------------------------------------------------

        /// <summary>保存してあるパスから関節の並びを組む。</summary>
        internal static bool TryBuild(Transform root, ChainStore.Chain chain, List<Transform> joints, out string error)
        {
            Transform start = AvatarRoots.Find(root, chain.start);
            Transform end = AvatarRoots.Find(root, chain.end);
            if (root != null && (start == null || end == null))
            {
                joints.Clear();
                error = "始点か終点がアバターの中に見つかりません（名前や階層が変わった可能性があります）。";
                return false;
            }
            return TransformChain.TryBuild(start, end, joints, out error);
        }

        /// <summary>選んだボーンにハンドルを置く指定が、今の鎖で有効か。</summary>
        internal static bool TryAnchorIndices(Transform root, ChainStore.Chain chain, List<Transform> joints, List<int> indices)
        {
            indices.Clear();
            if (chain.anchors.Count == 0) return false;

            Anchors.Clear();
            foreach (string path in chain.anchors) Anchors.Add(AvatarRoots.Find(root, path));
            return TransformChain.TryAnchorIndices(joints, Anchors, indices);
        }

        /// <summary>ハンドルを置く関節を決め、その位置・弧長パラメータ・関節番号を返す。</summary>
        private static Vector3[] PlaceControls(
            Transform root, ChainStore.Chain chain, List<Transform> joints, Vector3[] positions,
            out float[] knots, out int[] indices)
        {
            if (TryAnchorIndices(root, chain, joints, AnchorIndices))
            {
                Vector3[] placed = ChainSolver.PlaceControls(positions, AnchorIndices, out knots, out indices);
                if (placed.Length >= 2) return placed;
            }

            int[] even = ChainSolver.EvenJointIndices(positions, Mathf.Clamp(chain.divisions, 1, MaxDivisions));
            return ChainSolver.PlaceControls(positions, even, out knots, out indices);
        }

        private static int HandleCount(Transform root, ChainStore.Chain chain)
        {
            if (!TryBuild(root, chain, Joints, out _)) return 0;
            return PlaceControls(root, chain, Joints, Positions(Joints), out _, out _).Length;
        }

        private static Vector3[] Positions(List<Transform> joints)
        {
            var positions = new Vector3[joints.Count];
            for (int i = 0; i < positions.Length; i++) positions[i] = joints[i].position;
            return positions;
        }

        // ---------------------------------------------------------------
        // 選択
        // ---------------------------------------------------------------

        internal static void Select(string chain, int control)
        {
            _selectedChain = chain;
            _selectedControl = chain != null ? control : -1;

            // こちらのハンドルを選んでいる間は、Unity の移動・回転ギズモを隠す。
            // 選択中のボーンに出る Unity のギズモと同じ場所に重なり、どちらを掴んだか分からなくなるため。
            bool hide = chain != null && control >= 0;
            if (hide != _toolsHidden)
            {
                Tools.hidden = hide;
                _toolsHidden = hide;
            }
            SceneView.RepaintAll();
        }

        /// <summary>シーンで別のものを選んだら、こちらのハンドルの選択を外して Unity のギズモを戻す。</summary>
        private static void OnSelectionChanged()
        {
            if (_drag != null || _rotateUndoGroup >= 0) return;
            Select(null, -1);
        }

        // ---------------------------------------------------------------
        // シーン
        // ---------------------------------------------------------------

        private static void OnSceneGUI(SceneView view)
        {
            // 離したことを検知したら、掴んでいた操作を 1 回の Undo にまとめて終える。
            if (GUIUtility.hotControl == 0)
            {
                EndDrag();
                EndRotate();
            }

            ChainStore store = Store;
            if (!store.gizmosVisible) return;

            foreach (ChainStore.AvatarEntry avatar in store.avatars)
            {
                if (!avatar.visible) continue;
                Transform root = AvatarRoots.Resolve(avatar.key);
                if (root == null || !root.gameObject.activeInHierarchy) continue;

                foreach (ChainStore.Chain chain in avatar.chains)
                {
                    if (!chain.visible) continue;
                    if (!TryBuild(root, chain, Joints, out _)) continue;
                    DrawChain(root, chain);
                }
            }
        }

        private static void DrawChain(Transform root, ChainStore.Chain chain)
        {
            bool dragging = _drag != null && _drag.chain == chain.id;

            // 掴んでいなければ、ハンドルは今の鎖の上に置き直す。
            Vector3[] controls;
            float[] rolls;
            float[] knots;
            int[] indices;
            if (dragging)
            {
                controls = _drag.controls;
                rolls = _drag.rolls;
                knots = _drag.knots;
                indices = _drag.indices;
            }
            else
            {
                Vector3[] positions = Positions(Joints);
                if (ChainSolver.JointParameters(positions, out _) == null) return;
                controls = PlaceControls(root, chain, Joints, positions, out knots, out indices);
                if (controls.Length < 2) return;
                rolls = new float[controls.Length];
            }

            Event e = Event.current;
            if (e.type == EventType.Repaint) DrawLines(controls, knots);

            float handleScale = Store.handleScale;
            for (int k = 0; k < controls.Length; k++)
            {
                bool selected = _selectedChain == chain.id && _selectedControl == k;
                float u = ChainSolver.Knot(knots, controls.Length, k);
                Vector3 position = controls[k];
                float size = HandleUtility.GetHandleSize(position) * handleScale;

                // --- 点（根元は動かさない。クリックで選ぶだけ） ---
                int id = GUIUtility.GetControlID(HandleHash, FocusType.Passive);
                using (new Handles.DrawingScope(selected ? SelectedColor : k == 0 ? RootColor : HandleColor))
                {
                    if (k == 0)
                    {
                        if (Handles.Button(position, Quaternion.identity, size * 0.07f, size * 0.09f,
                                Handles.SphereHandleCap))
                        {
                            Select(chain.id, k);
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 moved = Handles.FreeMoveHandle(
                            id, position, size * 0.08f, Vector3.zero, Handles.SphereHandleCap);
                        if (GUIUtility.hotControl == id && !selected) Select(chain.id, k);
                        if (EditorGUI.EndChangeCheck()) MoveControl(root, chain, k, moved - position);
                    }
                }

                if (!selected) continue;

                // --- 回転ツールのとき: この関節だけを回す（FK） ---
                if (Tools.current == Tool.Rotate)
                {
                    if (!dragging) RotateJointHandle(Joints[indices[k]], position);
                    continue;
                }

                // --- 選択中: 軸つきの移動ハンドル ---
                if (k > 0)
                {
                    Quaternion handleRotation = Tools.pivotRotation == PivotRotation.Local
                        ? LookAlong(ChainSolver.SplineTangent(controls, u, knots))
                        : Quaternion.identity;

                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.PositionHandle(position, handleRotation);
                    if (EditorGUI.EndChangeCheck()) MoveControl(root, chain, k, moved - position);
                }

                // --- 選択中: ロール（曲線の接線まわりの円） ---
                Vector3 axis = ChainSolver.SplineTangent(controls, u, knots);
                if (axis.sqrMagnitude < 1e-12f) continue;
                axis.Normalize();

                // Disc は掴んだ時点の回転からの累積を返すので、今のロールを渡して
                // 戻り値を「新しいロール」として読み、差分だけ足す。
                float currentRoll = rolls[k];
                using (new Handles.DrawingScope(RollColor))
                {
                    EditorGUI.BeginChangeCheck();
                    Quaternion rolled = Handles.Disc(
                        Quaternion.AngleAxis(currentRoll, axis), position, axis, size * 0.6f, false, 0f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        rolled.ToAngleAxis(out float angle, out Vector3 rolledAxis);
                        if (Vector3.Dot(rolledAxis, axis) < 0f) angle = -angle;
                        RollControl(root, chain, k, Mathf.DeltaAngle(currentRoll, angle));
                    }
                }
            }
        }

        private static void DrawLines(Vector3[] controls, float[] knots)
        {
            using (new Handles.DrawingScope(ChainColor))
                Handles.DrawAAPolyLine(2f, Positions(Joints));

            // IK では曲線は鎖の形を決めないので、ハンドルを結ぶ補助線としてだけ薄く出す。
            bool ik = Store.mode == SolveMode.IK;
            Curve.Clear();
            int steps = (controls.Length - 1) * 12;
            for (int s = 0; s <= steps; s++) Curve.Add(ChainSolver.Spline(controls, (float)s / steps, knots));

            Color color = ik ? new Color(CurveColor.r, CurveColor.g, CurveColor.b, 0.3f) : CurveColor;
            using (new Handles.DrawingScope(color))
                Handles.DrawAAPolyLine(ik ? 2f : 3f, Curve.ToArray());
        }

        // ---------------------------------------------------------------
        // 操作
        // ---------------------------------------------------------------

        private static void MoveControl(Transform root, ChainStore.Chain chain, int control, Vector3 delta)
        {
            if (control <= 0 || delta.sqrMagnitude <= 0f) return;
            if (!BeginDrag(root, chain)) return;

            Drag d = _drag;
            ChainStore store = Store;
            if (store.mode == SolveMode.IK)
            {
                if (d.active < 0)
                {
                    d.active = control;
                    d.target = d.controls[control];
                }
                if (d.active != control) return;

                d.target += delta;
                if (store.keepRest)
                {
                    // 掴んだ所より先は、先のハンドルを元の位置へ、先端を元の向きへ戻す。
                    ChainIK.SolveKeepingRest(
                        d.positions, d.rotations, d.indices, control, d.target,
                        d.restPositions, d.restRotations, store.ikBias);
                }
                else
                {
                    ChainIK.Solve(d.positions, d.rotations, d.indices[control], d.target, store.ikBias);
                }

                // 他のハンドルは解いた鎖に乗せ、掴んでいるものだけ行き先に置く
                // （届かない位置でも、ハンドルはマウスに付いてくるように）。
                for (int k = 0; k < d.controls.Length; k++) d.controls[k] = d.positions[d.indices[k]];
                d.controls[control] = d.target;

                // 先を留めないなら、届かせた関節より先はローカル回転が変わらないので書かない。
                bool downstreamMoved = store.keepRest && control < d.indices.Length - 1;
                Write(downstreamMoved ? d.joints.Length : d.indices[control]);
                return;
            }

            int last = store.carryDownstream ? d.controls.Length - 1 : control;
            for (int k = control; k <= last; k++) d.controls[k] += delta;
            SolveCurve();
        }

        private static void RollControl(Transform root, ChainStore.Chain chain, int control, float degrees)
        {
            if (Mathf.Abs(degrees) <= 0f) return;
            if (!BeginDrag(root, chain)) return;

            Drag d = _drag;
            ChainStore store = Store;
            if (store.mode == SolveMode.IK)
            {
                // IK では、回したハンドルまでのねじれを根元から徐々に付け、先はそのまま運ぶ。
                // 親も一緒にねじれるので、掴んだ点だけが急にねじれることはない。
                float total = d.rolls[control] + degrees;
                float knot = d.knots[control];
                for (int k = 0; k < d.rolls.Length; k++)
                    d.rolls[k] = knot > 0f ? total * Mathf.Min(1f, d.knots[k] / knot) : total;
            }
            else
            {
                int last = store.carryDownstream ? d.rolls.Length - 1 : control;
                for (int k = control; k <= last; k++) d.rolls[k] += degrees;
            }
            SolveCurve();
        }

        /// <summary>
        /// 回転ギズモで関節 1 つだけを回す。親は動かず、子（鎖の先を含む）は形を保って付いてくる。
        /// ギズモの向きは Unity の Pivot Rotation（Local / Global）に従う。
        /// </summary>
        private static void RotateJointHandle(Transform joint, Vector3 position)
        {
            Quaternion current = joint.rotation;
            Quaternion shown = RotateTracker.Shown(
                Tools.pivotRotation == PivotRotation.Local ? current : Quaternion.identity);

            EditorGUI.BeginChangeCheck();
            Quaternion rotated = Handles.RotationHandle(shown, position);
            if (!EditorGUI.EndChangeCheck()) return;

            Quaternion delta = RotateTracker.Take(shown, rotated);

            if (_rotateUndoGroup < 0)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
                _rotateUndoGroup = Undo.GetCurrentGroup();
            }

            Undo.RecordObject(joint, UndoName);
            joint.rotation = delta * current;
        }

        private static void EndRotate()
        {
            RotateTracker.Reset();
            if (_rotateUndoGroup < 0) return;
            Undo.CollapseUndoOperations(_rotateUndoGroup);
            _rotateUndoGroup = -1;
        }

        /// <summary>掴んだ瞬間の鎖を覚える。以後はこの形を基準に解く。</summary>
        private static bool BeginDrag(Transform root, ChainStore.Chain chain)
        {
            if (_drag != null && _drag.chain == chain.id) return true;
            EndDrag();

            if (!TryBuild(root, chain, Joints, out _)) return false;

            int count = Joints.Count;
            var drag = new Drag
            {
                chain = chain.id,
                joints = Joints.ToArray(),
                restPositions = new Vector3[count],
                restRotations = new Quaternion[count],
                positions = new Vector3[count],
                rotations = new Quaternion[count],
            };
            for (int i = 0; i < count; i++)
            {
                drag.restPositions[i] = drag.positions[i] = Joints[i].position;
                drag.restRotations[i] = drag.rotations[i] = Joints[i].rotation;
            }
            if (ChainSolver.JointParameters(drag.restPositions, out _) == null) return false;

            drag.restControls = PlaceControls(root, chain, Joints, drag.restPositions, out drag.knots, out drag.indices);
            if (drag.restControls.Length < 2) return false;
            drag.controls = (Vector3[])drag.restControls.Clone();
            drag.rolls = new float[drag.controls.Length];

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            drag.undoGroup = Undo.GetCurrentGroup();

            _drag = drag;
            return true;
        }

        /// <summary>曲線に沿わせて解く（曲線モードの移動と、ロール）。</summary>
        private static void SolveCurve()
        {
            Drag d = _drag;
            if (!ChainSolver.Solve(
                    d.restPositions, d.restRotations, d.restControls, d.controls, d.rolls,
                    d.positions, d.rotations, d.knots))
                return;

            Write(d.joints.Length);
        }

        /// <summary>解いた回転を、根元から count 個の関節へ書き込む。</summary>
        private static void Write(int count)
        {
            Drag d = _drag;

            // 途中で消えた関節があれば諦める（Undo 記録に null を渡せない）。
            foreach (Transform t in d.joints)
            {
                if (t == null)
                {
                    EndDrag();
                    return;
                }
            }

            count = Mathf.Min(count, d.joints.Length);
            var targets = new Transform[count];
            Array.Copy(d.joints, targets, count);
            Undo.RecordObjects(targets, UndoName);

            // 回転だけを書く。親から順に書けば、子の位置は親の回転に付いてくる。
            for (int i = 0; i < count; i++) d.joints[i].rotation = d.rotations[i];
        }

        internal static void EndDrag()
        {
            if (_drag == null) return;
            Undo.CollapseUndoOperations(_drag.undoGroup);
            _drag = null;
            SceneView.RepaintAll();
        }

        private static Quaternion LookAlong(Vector3 tangent)
        {
            if (tangent.sqrMagnitude < 1e-12f) return Quaternion.identity;
            Vector3 up = Mathf.Abs(Vector3.Dot(tangent.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(tangent, up);
        }
    }
}
