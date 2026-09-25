using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// Transform の鎖（髪・尻尾・スカートなど）を、少数の IK ハンドルとロールハンドルで
    /// シーン上から動かす。
    ///
    /// 鎖は始点と終点で指定し、その間を弧長で「分割数」等分した点がハンドルになる。
    /// ハンドルを動かすと、ハンドルを通る曲線に沿って鎖が曲がる（解き方は
    /// <see cref="ChainSolver"/>）。
    ///
    /// 書き込むのは各関節の回転だけ。localPosition と localScale には触れないので、
    /// どう動かしてもボーンの長さとスケールは変わらない。曲線が鎖より長ければ先端は
    /// 届かずに止まり、短ければ曲線の先へはみ出す。
    ///
    /// ハンドルは掴んでいる間だけ自由に動き、離すと今の鎖の上へ置き直される。
    /// 状態を鎖の外に持たないので、Undo やアニメーションの時間移動、手で回した
    /// 後でも、ハンドルは常に見えている鎖の上から始まる。
    ///
    /// シーンやアバターには何も足さない（コンポーネントもアセットも作らない）。
    /// 鎖の指定はこのウィンドウが持ち、ウィンドウを閉じればハンドルも消える。
    /// </summary>
    internal sealed class ChainHandlesWindow : EditorWindow
    {
        private const string MenuPath = "YozoLab/Chain Handles";
        private const string UndoName = "Chain Handles";
        private const int MaxDivisions = 32;

        private static readonly Color ChainColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color CurveColor = new Color(0.35f, 0.85f, 1f, 0.8f);
        private static readonly Color HandleColor = new Color(0.35f, 0.85f, 1f, 0.9f);
        private static readonly Color RootColor = new Color(0.6f, 0.6f, 0.6f, 0.9f);
        private static readonly Color SelectedColor = new Color(1f, 0.78f, 0.25f, 1f);
        private static readonly Color RollColor = new Color(1f, 0.45f, 0.75f, 0.9f);

        private static readonly int HandleHash = "YozoLab.ChainHandles".GetHashCode();

        [Serializable]
        private sealed class ChainEntry
        {
            public Transform start;
            public Transform end;
            public int divisions = 3;
            public bool visible = true;
        }

        [SerializeField] private List<ChainEntry> chains = new List<ChainEntry>();
        [SerializeField] private bool carryDownstream;
        [SerializeField] private float handleScale = 1f;

        private Vector2 _scroll;

        // ---- 選択中のハンドル（シーン上でクリックしたもの） ----------------
        private int _selectedChain = -1;
        private int _selectedControl = -1;

        // ---- 掴んでいる間だけ意味を持つ状態 --------------------------------
        private sealed class Drag
        {
            public int chain;
            public Transform[] joints;
            public Vector3[] restPositions;
            public Quaternion[] restRotations;
            public Vector3[] restControls;
            public Vector3[] controls;
            public float[] rolls;
            public Vector3[] positions;
            public Quaternion[] rotations;
            public int undoGroup;
        }

        private Drag _drag;

        private readonly List<Transform> _joints = new List<Transform>();
        private readonly List<Vector3> _curve = new List<Vector3>();

        [MenuItem(MenuPath)]
        private static void Open() => GetWindow<ChainHandlesWindow>("Chain Handles");

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EndDrag();
            SceneView.RepaintAll();
        }

        private void OnUndoRedo()
        {
            Repaint();
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------
        // ウィンドウ
        // ---------------------------------------------------------------

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "始点と終点で鎖を指定すると、シーン上にハンドルが出ます。\n" +
                "階層ウィンドウの右クリック「YozoLab/Chain Handles で動かす」からも作れます。\n" +
                "・水色の点をドラッグ: 鎖をその方向へ曲げる（他の点は留まる）\n" +
                "・点をクリックで選択: 移動ハンドルと、ねじり（ロール）の円が出る\n" +
                "・灰色の根元の点: 動かせないが、選択してロールだけ回せる\n" +
                "変えるのは各ボーンの回転だけで、長さとスケールは変わりません。",
                MessageType.None);

            using (var scope = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scope.scrollPosition;
                for (int i = 0; i < chains.Count; i++)
                {
                    if (!DrawEntry(i)) break;
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 選択から鎖を追加"))
                {
                    Rect anchor = GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect());
                    ChainPickerWindow.StartFromSelection(anchor, (s, e) => AddOrFocus(s, e));
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("＋ 空の欄", GUILayout.Width(80f)))
                {
                    Undo.RecordObject(this, UndoName);
                    chains.Add(new ChainEntry());
                }
            }

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            bool carry = EditorGUILayout.ToggleLeft(
                new GUIContent("先のハンドルも一緒に動かす",
                    "ON: 掴んだ点より先の点も同じだけ動く（FK 寄り）。OFF: 他の点は留まる（IK 寄り）。"),
                carryDownstream);
            float scale = EditorGUILayout.Slider("ハンドルの大きさ", handleScale, 0.25f, 4f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, UndoName);
                carryDownstream = carry;
                handleScale = scale;
                SceneView.RepaintAll();
            }
        }

        /// <summary>1 件分の欄。項目を消したら false（リストが変わったので描画を打ち切る）。</summary>
        private bool DrawEntry(int index)
        {
            ChainEntry entry = chains[index];

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool visible = EditorGUILayout.ToggleLeft($"鎖 {index + 1}", entry.visible, EditorStyles.boldLabel);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(this, UndoName);
                        entry.visible = visible;
                        SceneView.RepaintAll();
                    }

                    if (GUILayout.Button("選択から", GUILayout.Width(70f)))
                    {
                        Rect anchor = GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect());
                        ChainPickerWindow.StartFromSelection(anchor, (s, e) =>
                        {
                            Undo.RecordObject(this, UndoName);
                            entry.start = s;
                            entry.end = e;
                            entry.divisions = DefaultDivisions(s, e);
                            Select(chains.IndexOf(entry), entry.divisions);
                            Repaint();
                        });
                        GUIUtility.ExitGUI();
                    }

                    if (GUILayout.Button("×", GUILayout.Width(22f)))
                    {
                        Undo.RecordObject(this, UndoName);
                        if (_drag != null && _drag.chain == index) EndDrag();
                        chains.RemoveAt(index);
                        _selectedChain = _selectedControl = -1;
                        SceneView.RepaintAll();
                        return false;
                    }
                }

                EditorGUI.BeginChangeCheck();
                var start = (Transform)EditorGUILayout.ObjectField("始点", entry.start, typeof(Transform), true);
                var end = (Transform)EditorGUILayout.ObjectField("終点", entry.end, typeof(Transform), true);
                int divisions = EditorGUILayout.IntSlider(
                    new GUIContent("分割数", "鎖を何等分してハンドルを置くか。ハンドルは根元を含めて分割数 + 1 個。"),
                    entry.divisions, 1, MaxDivisions);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(this, UndoName);
                    entry.start = start;
                    entry.end = end;
                    entry.divisions = divisions;
                    if (_selectedChain == index && _selectedControl > divisions) _selectedControl = -1;
                    SceneView.RepaintAll();
                }

                if (!TransformChain.TryBuild(entry.start, entry.end, _joints, out string error))
                {
                    EditorGUILayout.HelpBox(error, MessageType.Warning);
                    return true;
                }

                float length = 0f;
                for (int i = 1; i < _joints.Count; i++)
                    length += Vector3.Distance(_joints[i - 1].position, _joints[i].position);
                EditorGUILayout.LabelField($"関節 {_joints.Count} 個 / 全長 {length:0.###}");

                if (TransformChain.HasNonUniformScale(_joints))
                {
                    EditorGUILayout.HelpBox(
                        "鎖に非一様スケールが掛かっています。長さとスケールは変わりませんが、" +
                        "曲がり具合がハンドルとわずかにずれることがあります。",
                        MessageType.Info);
                }
            }
            return true;
        }

        /// <summary>関節数に応じた初期の分割数。短い鎖で細かく割っても扱いにくいだけなので。</summary>
        internal static int DefaultDivisions(int jointCount) => Mathf.Clamp((jointCount - 1) / 2, 1, 3);

        private static int DefaultDivisions(Transform start, Transform end)
        {
            var joints = new List<Transform>();
            if (!TransformChain.TryBuild(start, end, joints, out _)) return 3;
            return DefaultDivisions(joints.Count);
        }

        /// <summary>
        /// 鎖を決めてすぐ動かせる状態にする（右クリックや候補一覧から）。
        /// ウィンドウを開き、同じ鎖が既にあればそれを、無ければ足して、先端のハンドルを選ぶ。
        /// シーンビューで続けて操作できるよう、ウィンドウにはフォーカスを移さない。
        /// </summary>
        internal static void StartChain(Transform start, Transform end)
        {
            var window = GetWindow<ChainHandlesWindow>("Chain Handles", false);
            window.AddOrFocus(start, end);
        }

        private void AddOrFocus(Transform start, Transform end)
        {
            Undo.RecordObject(this, UndoName);

            int index = chains.FindIndex(c => c.start == start && c.end == end);
            if (index < 0)
            {
                chains.Add(new ChainEntry { start = start, end = end, divisions = DefaultDivisions(start, end) });
                index = chains.Count - 1;
            }

            chains[index].visible = true;
            Select(index, chains[index].divisions);
            Repaint();
        }

        // ---------------------------------------------------------------
        // シーン
        // ---------------------------------------------------------------

        private void OnSceneGUI(SceneView view)
        {
            // 離したことを検知したら、掴んでいた操作を 1 回の Undo にまとめて終える。
            if (_drag != null && GUIUtility.hotControl == 0) EndDrag();

            for (int i = 0; i < chains.Count; i++)
            {
                ChainEntry entry = chains[i];
                if (!entry.visible) continue;
                if (!TransformChain.TryBuild(entry.start, entry.end, _joints, out _)) continue;
                DrawChain(i, entry);
            }
        }

        private void DrawChain(int index, ChainEntry entry)
        {
            bool dragging = _drag != null && _drag.chain == index;
            int divisions = Mathf.Clamp(entry.divisions, 1, MaxDivisions);

            // 掴んでいなければ、ハンドルは今の鎖の上に置き直す。
            Vector3[] controls;
            float[] rolls;
            if (dragging)
            {
                controls = _drag.controls;
                rolls = _drag.rolls;
            }
            else
            {
                var positions = new Vector3[_joints.Count];
                for (int i = 0; i < positions.Length; i++) positions[i] = _joints[i].position;
                if (ChainSolver.JointParameters(positions, out _) == null) return;
                controls = ChainSolver.PlaceControls(positions, divisions);
                rolls = new float[controls.Length];
            }

            Event e = Event.current;
            if (e.type == EventType.Repaint) DrawLines(controls);

            for (int k = 0; k < controls.Length; k++)
            {
                bool selected = _selectedChain == index && _selectedControl == k;
                float u = (float)k / (controls.Length - 1);
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
                            Select(index, k);
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 moved = Handles.FreeMoveHandle(
                            id, position, size * 0.08f, Vector3.zero, Handles.SphereHandleCap);
                        if (GUIUtility.hotControl == id && !selected) Select(index, k);
                        if (EditorGUI.EndChangeCheck()) MoveControl(index, divisions, k, moved - position);
                    }
                }

                if (!selected) continue;

                // --- 選択中: 軸つきの移動ハンドル ---
                if (k > 0)
                {
                    Quaternion handleRotation = Tools.pivotRotation == PivotRotation.Local
                        ? LookAlong(ChainSolver.SplineTangent(controls, u))
                        : Quaternion.identity;

                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.PositionHandle(position, handleRotation);
                    if (EditorGUI.EndChangeCheck()) MoveControl(index, divisions, k, moved - position);
                }

                // --- 選択中: ロール（曲線の接線まわりの円） ---
                Vector3 axis = ChainSolver.SplineTangent(controls, u);
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
                        RollControl(index, divisions, k, Mathf.DeltaAngle(currentRoll, angle));
                    }
                }
            }
        }

        private void DrawLines(Vector3[] controls)
        {
            var points = new Vector3[_joints.Count];
            for (int i = 0; i < points.Length; i++) points[i] = _joints[i].position;

            using (new Handles.DrawingScope(ChainColor))
                Handles.DrawAAPolyLine(2f, points);

            _curve.Clear();
            int steps = (controls.Length - 1) * 12;
            for (int s = 0; s <= steps; s++) _curve.Add(ChainSolver.Spline(controls, (float)s / steps));

            using (new Handles.DrawingScope(CurveColor))
                Handles.DrawAAPolyLine(3f, _curve.ToArray());
        }

        private void Select(int chain, int control)
        {
            _selectedChain = chain;
            _selectedControl = control;
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------
        // 操作
        // ---------------------------------------------------------------

        private void MoveControl(int chain, int divisions, int control, Vector3 delta)
        {
            if (control <= 0 || delta.sqrMagnitude <= 0f) return;
            if (!BeginDrag(chain, divisions)) return;

            int last = carryDownstream ? _drag.controls.Length - 1 : control;
            for (int k = control; k <= last; k++) _drag.controls[k] += delta;
            Apply();
        }

        private void RollControl(int chain, int divisions, int control, float degrees)
        {
            if (Mathf.Abs(degrees) <= 0f) return;
            if (!BeginDrag(chain, divisions)) return;

            int last = carryDownstream ? _drag.rolls.Length - 1 : control;
            for (int k = control; k <= last; k++) _drag.rolls[k] += degrees;
            Apply();
        }

        /// <summary>掴んだ瞬間の鎖を覚える。以後はこの形を基準に解く。</summary>
        private bool BeginDrag(int chain, int divisions)
        {
            if (_drag != null && _drag.chain == chain) return true;
            EndDrag();

            if (!TransformChain.TryBuild(chains[chain].start, chains[chain].end, _joints, out _)) return false;

            int count = _joints.Count;
            var drag = new Drag
            {
                chain = chain,
                joints = _joints.ToArray(),
                restPositions = new Vector3[count],
                restRotations = new Quaternion[count],
                positions = new Vector3[count],
                rotations = new Quaternion[count],
            };
            for (int i = 0; i < count; i++)
            {
                drag.restPositions[i] = _joints[i].position;
                drag.restRotations[i] = _joints[i].rotation;
            }
            if (ChainSolver.JointParameters(drag.restPositions, out _) == null) return false;

            drag.restControls = ChainSolver.PlaceControls(drag.restPositions, divisions);
            drag.controls = (Vector3[])drag.restControls.Clone();
            drag.rolls = new float[drag.controls.Length];

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            drag.undoGroup = Undo.GetCurrentGroup();

            _drag = drag;
            return true;
        }

        private void Apply()
        {
            Drag d = _drag;
            if (!ChainSolver.Solve(
                    d.restPositions, d.restRotations, d.restControls, d.controls, d.rolls,
                    d.positions, d.rotations))
                return;

            // 途中で消えた関節があれば諦める（Undo 記録に null を渡せない）。
            foreach (Transform t in d.joints)
            {
                if (t == null)
                {
                    EndDrag();
                    return;
                }
            }

            Undo.RecordObjects(d.joints, UndoName);

            // 回転だけを書く。親から順に書けば、子の位置は親の回転に付いてくる。
            for (int i = 0; i < d.joints.Length; i++) d.joints[i].rotation = d.rotations[i];
        }

        private void EndDrag()
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
