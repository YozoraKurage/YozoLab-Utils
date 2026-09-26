using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// 選んだオブジェクトから作れる鎖の候補を並べ、1 つ選ばせるドロップダウン。
    ///
    /// 候補が 1 つならこの窓は出さずにそのまま決める。複数あるときだけ一覧にし、
    /// 行にマウスを乗せる（または上下キーで選ぶ）と、その鎖がシーン上に
    /// 太線で浮かぶ。関節の位置と、作られるハンドルの位置（中抜きの円）も出すので、
    /// 決める前に「どこからどこまでを、何点で動かすことになるか」が見える。
    /// 他の候補は薄い線で重ねて出す。
    ///
    /// 窓はフォーカスを失うと閉じる（ShowAsDropDown）。プレビューも一緒に消える。
    /// </summary>
    internal sealed class ChainPickerWindow : EditorWindow
    {
        private const string MenuPath = "GameObject/YozoLab/Chain Handles で動かす";
        private const float RowHeight = 20f;
        private const float HeaderHeight = 22f;
        private const float Width = 360f;
        private const float MaxHeight = 420f;

        private static readonly Color FaintColor = new Color(0.35f, 0.85f, 1f, 0.25f);
        private static readonly Color ActiveColor = new Color(1f, 0.78f, 0.25f, 1f);
        private static readonly Color ControlColor = new Color(0.35f, 0.85f, 1f, 1f);

        private sealed class Entry
        {
            public Transform start;
            public Transform end;
            public Transform[] joints;
            public string label;
            public string detail;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private Action<Transform, Transform, List<Transform>> _onPick;
        private string _title;
        private int _hovered = -1;
        private int _cursor;
        private Vector2 _scroll;

        private static GUIStyle _labelStyle;
        private static GUIStyle _detailStyle;
        private static GUIStyle _sceneLabelStyle;

        // ---------------------------------------------------------------
        // 入口
        // ---------------------------------------------------------------

        private static bool _menuPending;

        /// <summary>
        /// 階層ウィンドウの右クリック（と GameObject メニュー）から。
        /// 複数選択していると選択数だけ呼ばれるので、1 回にまとめて次のフレームで動く。
        /// </summary>
        [MenuItem(MenuPath, false, 49)]
        private static void FromMenu(MenuCommand command)
        {
            if (_menuPending) return;
            _menuPending = true;
            EditorApplication.delayCall += () =>
            {
                _menuPending = false;
                StartFromSelection(SceneCorner(), ChainHandlesScene.StartChain);
            };
        }

        [MenuItem(MenuPath, true)]
        private static bool FromMenuValidate()
        {
            return TransformChain.SelectedTransforms().Length > 0;
        }

        /// <summary>
        /// 今の選択から鎖を決める。
        /// - 3 つ以上: 一本の親子の並びに乗っていれば、一番上から一番下までを鎖にし、
        ///   選んだボーンそれぞれにハンドルを置く。
        /// - 親子の 2 つ: それを始点と終点にする（ハンドルは分割数で等分）。
        /// - 1 つ: 候補を探し、1 つに絞れればそのまま、複数なら一覧を出す。
        /// </summary>
        /// <param name="screenRect">一覧を出すときの基準（この下に開く）。スクリーン座標。</param>
        internal static void StartFromSelection(Rect screenRect, Action<Transform, Transform, List<Transform>> onPick)
        {
            Transform[] selected = TransformChain.SelectedTransforms();

            if (selected.Length >= 3)
            {
                if (ChainCandidates.TryOrderOnLine(selected, out List<Transform> ordered))
                    onPick(ordered[0], ordered[ordered.Count - 1], ordered);
                else
                    Notify("選んだボーンが一本の親子の並びに乗っていません");
                return;
            }

            if (selected.Length == 2)
            {
                if (TransformChain.TryGuess(selected, out Transform s, out Transform e)) onPick(s, e, null);
                else Notify("選んだ 2 つが親子関係にありません");
                return;
            }

            Transform target = Selection.activeTransform != null ? Selection.activeTransform
                : selected.Length == 1 ? selected[0] : null;
            if (target == null)
            {
                Notify("鎖にしたいオブジェクトを選んでください");
                return;
            }

            List<ChainCandidates.Candidate<Transform>> found = ChainCandidates.Find(target);
            if (found.Count == 0)
            {
                Notify($"{target.name} からは鎖を作れませんでした（子も一本道の親もありません）");
                return;
            }
            if (found.Count == 1)
            {
                onPick(found[0].start, found[0].end, null);
                return;
            }

            var window = CreateInstance<ChainPickerWindow>();
            window.Setup(target, found, onPick);
            float height = Mathf.Min(MaxHeight, HeaderHeight + found.Count * RowHeight + 6f);
            window.ShowAsDropDown(screenRect, new Vector2(Width, height));
        }

        /// <summary>シーンビューの左上。プレビューを見ながら選べる位置に一覧を出す。</summary>
        private static Rect SceneCorner()
        {
            EditorWindow anchor = SceneView.lastActiveSceneView;
            if (anchor == null) anchor = focusedWindow;
            if (anchor == null) return new Rect(200f, 200f, 1f, 1f);

            Rect p = anchor.position;
            return new Rect(p.x + 12f, p.y + 40f, 1f, 1f);
        }

        /// <summary>
        /// 鎖を作れなかった理由を伝える。シーンビュー中央の大きな通知は作業の邪魔になるので、
        /// コンソールに出すだけにする。
        /// </summary>
        private static void Notify(string message) => Debug.LogWarning($"[Chain Handles] {message}");

        private void Setup(Transform target, List<ChainCandidates.Candidate<Transform>> found, Action<Transform, Transform, List<Transform>> onPick)
        {
            _onPick = onPick;
            _title = $"{target.name} から作れる鎖（{found.Count} 件）";

            var joints = new List<Transform>();
            foreach (ChainCandidates.Candidate<Transform> c in found)
            {
                if (!TransformChain.TryBuild(c.start, c.end, joints, out _)) continue;
                _entries.Add(new Entry
                {
                    start = c.start,
                    end = c.end,
                    joints = joints.ToArray(),
                    label = $"{c.start.name}  →  {c.end.name}",
                    detail = $"{joints.Count} 関節 / {ChainCandidates.ChainLength(joints):0.###}",
                });
            }
        }

        // ---------------------------------------------------------------
        // 窓
        // ---------------------------------------------------------------

        private void OnEnable()
        {
            wantsMouseMove = true;
            wantsMouseEnterLeaveWindow = true;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.RepaintAll();
        }

        private int Active => _hovered >= 0 ? _hovered : _cursor;

        private void OnGUI()
        {
            EnsureStyles();
            Event e = Event.current;
            HandleKeys(e);

            GUI.Label(new Rect(6f, 2f, position.width - 12f, HeaderHeight - 2f), _title, EditorStyles.boldLabel);

            var view = new Rect(0f, HeaderHeight, position.width, position.height - HeaderHeight);
            var content = new Rect(0f, 0f, position.width - 16f, _entries.Count * RowHeight);

            int hovered = -1;
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            for (int i = 0; i < _entries.Count; i++)
            {
                var row = new Rect(0f, i * RowHeight, content.width, RowHeight);
                bool over = row.Contains(e.mousePosition);
                if (over) hovered = i;

                if (e.type == EventType.Repaint)
                {
                    if (i == Active) EditorGUI.DrawRect(row, new Color(0.24f, 0.49f, 0.91f, 0.45f));
                    GUI.Label(new Rect(row.x + 6f, row.y, row.width - 110f, row.height), _entries[i].label, _labelStyle);
                    GUI.Label(new Rect(row.xMax - 104f, row.y, 100f, row.height), _entries[i].detail, _detailStyle);
                }

                if (over && e.type == EventType.MouseDown && e.button == 0)
                {
                    Pick(i);
                    e.Use();
                    GUIUtility.ExitGUI();
                }
            }
            GUI.EndScrollView();

            if (e.type == EventType.MouseLeaveWindow) hovered = -1;
            if (e.type == EventType.MouseMove || e.type == EventType.MouseLeaveWindow || e.type == EventType.ScrollWheel)
                SetHovered(hovered);
        }

        private void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;

            switch (e.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveCursor(-1);
                    e.Use();
                    break;
                case KeyCode.DownArrow:
                    MoveCursor(1);
                    e.Use();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Pick(Active);
                    e.Use();
                    GUIUtility.ExitGUI();
                    break;
                case KeyCode.Escape:
                    Close();
                    e.Use();
                    GUIUtility.ExitGUI();
                    break;
            }
        }

        private void MoveCursor(int step)
        {
            if (_entries.Count == 0) return;
            _cursor = Mathf.Clamp(Active + step, 0, _entries.Count - 1);
            _hovered = -1;

            // カーソル行が見えるようにスクロールを追従させる。
            float top = _cursor * RowHeight;
            float visible = position.height - HeaderHeight;
            if (top < _scroll.y) _scroll.y = top;
            else if (top + RowHeight > _scroll.y + visible) _scroll.y = top + RowHeight - visible;

            Repaint();
            SceneView.RepaintAll();
        }

        private void SetHovered(int hovered)
        {
            if (hovered == _hovered) return;
            _hovered = hovered;
            if (hovered >= 0) _cursor = hovered;
            Repaint();
            SceneView.RepaintAll();
        }

        private void Pick(int index)
        {
            if (index < 0 || index >= _entries.Count) return;
            Entry entry = _entries[index];
            Action<Transform, Transform, List<Transform>> onPick = _onPick;
            Close();
            if (entry.start != null && entry.end != null) onPick?.Invoke(entry.start, entry.end, null);
        }

        private static void EnsureStyles()
        {
            if (_labelStyle != null) return;
            _labelStyle = new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip };
            _detailStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        }

        // ---------------------------------------------------------------
        // シーン上のプレビュー
        // ---------------------------------------------------------------

        private void OnSceneGUI(SceneView view)
        {
            if (Event.current.type != EventType.Repaint) return;

            int active = Active;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i == active) continue;
                Vector3[] points = Positions(_entries[i].joints);
                if (points == null) continue;
                using (new Handles.DrawingScope(FaintColor))
                    Handles.DrawAAPolyLine(2f, points);
            }

            if (active < 0 || active >= _entries.Count) return;
            DrawActive(_entries[active]);
        }

        private static void DrawActive(Entry entry)
        {
            Vector3[] points = Positions(entry.joints);
            if (points == null) return;

            using (new Handles.DrawingScope(ActiveColor))
            {
                Handles.DrawAAPolyLine(6f, points);
                for (int i = 0; i < points.Length; i++)
                {
                    float size = HandleUtility.GetHandleSize(points[i]) * (i == 0 || i == points.Length - 1 ? 0.07f : 0.035f);
                    Handles.SphereHandleCap(0, points[i], Quaternion.identity, size, EventType.Repaint);
                }
            }

            // 実際に作られるハンドルの位置（既定の分割数）。
            if (ChainSolver.JointParameters(points, out _) != null)
            {
                Vector3[] controls = ChainSolver.PlaceControls(points, ChainHandlesScene.DefaultDivisions(points.Length), out _);
                using (new Handles.DrawingScope(ControlColor))
                {
                    foreach (Vector3 c in controls)
                    {
                        float size = HandleUtility.GetHandleSize(c) * 0.1f;
                        Handles.CircleHandleCap(0, c, Camera.current.transform.rotation, size, EventType.Repaint);
                    }
                }
            }

            if (_sceneLabelStyle == null)
            {
                _sceneLabelStyle = new GUIStyle(EditorStyles.whiteMiniLabel)
                {
                    normal = { background = Texture2D.grayTexture },
                    padding = new RectOffset(4, 4, 1, 1),
                };
            }
            Handles.Label(points[0], $"始点 {entry.start.name}", _sceneLabelStyle);
            Handles.Label(points[points.Length - 1], $"終点 {entry.end.name}", _sceneLabelStyle);
        }

        private static Vector3[] Positions(Transform[] joints)
        {
            var points = new Vector3[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null) return null;
                points[i] = joints[i].position;
            }
            return points;
        }
    }
}
