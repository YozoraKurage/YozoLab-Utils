// WindowSwitcher.cs
// デフォルトショートカット: / (スラッシュ)
//   Edit > Shortcuts... の "Tools/Window Switcher" で変更可能。
//   Window > Window Switcher からメニュー経由でも開けます。
//
// 今開いている全EditorWindowを一覧表示し、選択したウィンドウを最前面に出してフォーカスします。
// 埋もれやすいフローティングウィンドウを上に、ドック済みタブを下に表示します。
//
// 操作: 文字入力で絞り込み / ↑↓で選択 / Enterで決定 / Esc・フォーカス喪失で閉じる
//       行をマウスでホバーすると、そのウィンドウが背後で最前面に出ます(プレビュー)。
//       WindowSwitcher 自身は ShowUtility() のユーティリティウィンドウなので、
//       プレビュー中も常に最上面に残ります。
//
// プレビューで動かしたウィンドウの状態(ドックのタブ選択・フローティングの重なり順・
// フォーカス)は、開いた時点のスナップショットから閉じるときに復元します。
//   ・ウィンドウを選んだとき   → 全部戻してから、選んだものだけを前面へ
//   ・Escで閉じたとき          → 全部戻して、開く前のフォーカスへ
//   ・他のウィンドウをクリック → そのウィンドウは触らず、それ以外を戻す
//                                (クリックを打ち消さないため)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

public class WindowSwitcher : EditorWindow
{
    struct Entry
    {
        public EditorWindow window;
        public string label;
        public bool docked;
    }

    // プレビューでフォーカスが往復する間、自動クローズを止めておく時間。
    const double kFocusGrace = 0.5;

    List<Entry> _entries = new List<Entry>();
    string _search = "";
    int _selected;
    Vector2 _scroll;
    bool _focusedSearch;

    EditorWindow _previewed;
    EditorWindow _pendingPreview;
    EditorWindow _pendingActivate;
    double _ignoreLostFocusUntil;

    LayoutSnapshot _snapshot;
    bool _handledOnClose;
    bool _dismissedByFocusLoss;

    // ------------------------------------------------------------------ 起動
    [MenuItem("Window/Window Switcher")]
    static void OpenFromMenu() => Open();

    [Shortcut("Tools/Window Switcher", KeyCode.Slash)]
    public static void Open()
    {
        // 二重起動を防ぐ (検索欄に "/" を打った拍子に再入した場合など)
        foreach (var existing in Resources.FindObjectsOfTypeAll<WindowSwitcher>())
            if (existing != null) existing.Close();

        // 自分を作る前に現在の状態を控える
        var snapshot = LayoutSnapshot.Capture();

        var w = CreateInstance<WindowSwitcher>();
        w._snapshot = snapshot;
        w.titleContent = new GUIContent("Windows");

        var main = EditorGUIUtility.GetMainWindowPosition();
        const float width = 420f, height = 380f;
        w.position = new Rect(
            main.x + (main.width - width) * 0.5f,
            main.y + (main.height - height) * 0.35f,
            width, height);

        w.ShowUtility(); // ユーティリティウィンドウ = 常に最上面
        w.Focus();
    }

    void OnEnable()
    {
        CollectWindows();
        EditorApplication.update += FocusWatchdog;
    }

    void OnDisable()
    {
        EditorApplication.update -= FocusWatchdog;

        // ウィンドウを選んで閉じた場合は FlushActions 側で復元済み
        if (_handledOnClose) return;
        // 一度もプレビューしていなければ動かしたものが無いので何もしない
        if (_previewed == null) return;

        var snap = _snapshot;
        bool byFocusLoss = _dismissedByFocusLoss;
        EditorApplication.delayCall += () =>
        {
            // 他のウィンドウをクリックして閉じた場合は、その移った先を「ユーザーの選択」と
            // みなして尊重し、それ以外だけを元に戻す。フォーカスが Unity の外に出ていた
            // (focusedWindow == null) ときは、勝手に Unity を前面に引き戻さない。
            // Esc で閉じた場合は開く前のフォーカスに戻す。
            var winner = byFocusLoss ? focusedWindow : snap?.FocusedAtCapture;
            LayoutSnapshot.Restore(snap, winner, false);
        };
    }

    void OnLostFocus()
    {
        // プレビューでフォーカスが一瞬離れるのは無視する
        if (EditorApplication.timeSinceStartup < _ignoreLostFocusUntil) return;
        _dismissedByFocusLoss = true;
        Close(); // Spotlight風: フォーカスが外れたら閉じる
    }

    /// <summary>
    /// プレビュー後にフォーカスを取り戻せなかったときの保険。
    /// OnLostFocus は一度しか飛んでこないので、それを握り潰した場合の後始末をここで行う。
    /// </summary>
    void FocusWatchdog()
    {
        if (_previewed == null) return; // 一度もプレビューしていなければ OnLostFocus に任せる
        if (EditorApplication.timeSinceStartup < _ignoreLostFocusUntil) return;
        if (hasFocus) return;

        _dismissedByFocusLoss = true;
        Close();
    }

    // ------------------------------------------------------------------ 一覧
    void CollectWindows()
    {
        _entries = Resources.FindObjectsOfTypeAll<EditorWindow>()
            .Where(w => w != null && !(w is WindowSwitcher))
            .Select(w => new Entry
            {
                window = w,
                docked = w.docked,
                label = string.IsNullOrEmpty(w.titleContent.text)
                    ? w.GetType().Name
                    : w.titleContent.text
            })
            // フローティング(埋もれやすい)を先頭に、あとは名前順
            .OrderBy(e => e.docked)
            .ThenBy(e => e.label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    List<Entry> Filtered()
    {
        if (string.IsNullOrWhiteSpace(_search)) return _entries;
        var q = _search.Trim();
        return _entries
            .Where(e =>
                e.label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.window.GetType().Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    /// <summary>ホバー中の行が変わったら、そのウィンドウをプレビュー対象として予約する。</summary>
    void UpdateHover(int hovered, List<Entry> list)
    {
        if (hovered < 0 || hovered >= list.Count) return;

        var target = list[hovered].window;
        if (target == null || target == _previewed) return;

        _selected = hovered;
        _previewed = target;
        _pendingPreview = target;
    }

    /// <summary>
    /// ウィンドウの開閉・フォーカス操作は GUI 描画の途中でやると壊れるので、
    /// OnGUI の最後にまとめて実行する。
    /// </summary>
    void FlushActions()
    {
        if (_pendingActivate != null)
        {
            var target = _pendingActivate;
            _pendingActivate = null;

            // プレビューで何も動かしていないなら復元は不要
            var snap = _previewed != null ? _snapshot : null;
            _handledOnClose = true;

            Close();
            // 全部いったん元に戻してから、選ばれたものだけを前面に出す
            EditorApplication.delayCall += () => LayoutSnapshot.Restore(snap, target, true);
            return;
        }

        if (_pendingPreview != null)
        {
            var target = _pendingPreview;
            _pendingPreview = null;
            _ignoreLostFocusUntil = EditorApplication.timeSinceStartup + kFocusGrace;

            var self = this;
            EditorApplication.delayCall += () =>
            {
                // Show() は呼ばない。未表示のウィンドウをホバーしただけで開いてしまうため。
                // Focus() だけで既存ウィンドウの前面化 / ドックのタブ切り替えは行われる。
                if (target != null) target.Focus();
                if (self != null) self.Focus(); // 自分にフォーカスを戻して最上面を維持
            };
        }
    }

    // ------------------------------------------------------------------ GUI
    void OnGUI()
    {
        var list = Filtered();
        _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, list.Count - 1));

        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    return;
                case KeyCode.DownArrow:
                    _selected = Mathf.Min(_selected + 1, list.Count - 1);
                    e.Use();
                    break;
                case KeyCode.UpArrow:
                    _selected = Mathf.Max(_selected - 1, 0);
                    e.Use();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_selected >= 0 && _selected < list.Count)
                        _pendingActivate = list[_selected].window;
                    e.Use();
                    break;
            }
        }

        // 検索フィールド(開いた直後から入力可能)
        GUI.SetNextControlName("ws_search");
        var newSearch = EditorGUILayout.TextField(_search);
        if (newSearch != _search)
        {
            _search = newSearch;
            _selected = 0;
        }
        if (!_focusedSearch)
        {
            EditorGUI.FocusTextInControl("ws_search");
            _focusedSearch = true;
        }

        EditorGUILayout.Space(4);

        int hovered = -1;

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        bool? lastDocked = null;
        for (int i = 0; i < list.Count; i++)
        {
            if (lastDocked != list[i].docked)
            {
                lastDocked = list[i].docked;
                EditorGUILayout.LabelField(
                    lastDocked.Value ? "ドック済み" : "フローティング",
                    EditorStyles.miniBoldLabel);
            }

            bool isSelected = (i == _selected);
            var style = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(12, 4, 3, 3),
                normal =
                {
                    textColor = isSelected ? Color.white
                                           : GUI.skin.label.normal.textColor
                }
            };

            var content = new GUIContent(
                $"{list[i].label}  <{list[i].window.GetType().Name}>");
            var rect = GUILayoutUtility.GetRect(content, style, GUILayout.ExpandWidth(true));

            // rect が確定しているのは Repaint のときだけ
            if (e.type == EventType.Repaint && rect.Contains(e.mousePosition))
                hovered = i;

            if (isSelected)
                EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.90f, 0.85f));

            // ここで Close() すると BeginScrollView と対応が取れなくなるので予約だけする
            if (GUI.Button(rect, content, style))
                _pendingActivate = list[i].window;
        }
        if (list.Count == 0)
            EditorGUILayout.HelpBox("該当なし", MessageType.None);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.LabelField(
            "↑↓: 選択   Enter: 決定   Esc: 閉じる   ホバー: プレビュー",
            EditorStyles.centeredGreyMiniLabel);

        if (e.type == EventType.Repaint)
            UpdateHover(hovered, list);

        FlushActions();

        Repaint();
    }

    // ==================================================================
    // レイアウトのスナップショット / 復元
    //
    // 使っている内部APIはすべてリフレクション経由 + try/catch で、
    // 取れなければその項目だけ諦めて動作は継続する。
    // ==================================================================
    class LayoutSnapshot
    {
        // DockArea -> 選択されていたタブの index
        readonly List<KeyValuePair<UnityEngine.Object, int>> _dockTabs =
            new List<KeyValuePair<UnityEngine.Object, int>>();

        // ContainerWindow を前面から背面の順に並べたもの
        readonly List<UnityEngine.Object> _containersFrontToBack = new List<UnityEngine.Object>();

        EditorWindow _focused;

        /// <summary>WindowSwitcher を開く直前にフォーカスされていたウィンドウ。</summary>
        public EditorWindow FocusedAtCapture => _focused;

        // ---------------- リフレクション ----------------
        static readonly Type s_DockAreaType = FindEditorType("UnityEditor.DockArea");
        static readonly Type s_ViewType = FindEditorType("UnityEditor.View");
        static readonly Type s_ContainerWindowType = FindEditorType("UnityEditor.ContainerWindow");

        static readonly FieldInfo s_HostViewField =
            typeof(EditorWindow).GetField("m_Parent", BindingFlags.NonPublic | BindingFlags.Instance);
        // DockArea.selected は internal クラスの public プロパティ (getter/setter とも public)
        static readonly PropertyInfo s_SelectedProp =
            s_DockAreaType?.GetProperty("selected", BindingFlags.Public | BindingFlags.Instance);
        // View.window -> その View が乗っている ContainerWindow
        static readonly PropertyInfo s_ViewWindowProp =
            s_ViewType?.GetProperty("window", BindingFlags.Public | BindingFlags.Instance);
        // ContainerWindow.windows は getter 内で z 順に組み直されて返る
        static readonly PropertyInfo s_WindowsProp =
            s_ContainerWindowType?.GetProperty("windows", BindingFlags.Public | BindingFlags.Static);
        static readonly MethodInfo s_MoveBehindOf =
            s_ContainerWindowType?.GetMethod("MoveBehindOf", BindingFlags.Public | BindingFlags.Instance);

        static Type FindEditorType(string fullName)
        {
            var t = typeof(EditorWindow).Assembly.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        // ---------------- 取得 ----------------
        public static LayoutSnapshot Capture()
        {
            var snap = new LayoutSnapshot();
            try { snap._focused = focusedWindow; } catch { }
            snap.CaptureDockTabs();
            snap.CaptureContainerOrder();
            return snap;
        }

        void CaptureDockTabs()
        {
            if (s_DockAreaType == null || s_SelectedProp == null) return;
            try
            {
                foreach (var dock in Resources.FindObjectsOfTypeAll(s_DockAreaType))
                {
                    if (dock == null) continue;
                    try
                    {
                        _dockTabs.Add(new KeyValuePair<UnityEngine.Object, int>(
                            dock, (int)s_SelectedProp.GetValue(dock)));
                    }
                    catch { }
                }
            }
            catch { }
        }

        void CaptureContainerOrder()
        {
            if (s_WindowsProp == null || s_MoveBehindOf == null) return;
            try
            {
                var arr = s_WindowsProp.GetValue(null) as Array;
                if (arr == null || arr.Length < 2) return;

                var list = new List<UnityEngine.Object>();
                foreach (var o in arr)
                {
                    var uo = o as UnityEngine.Object;
                    if (uo != null) list.Add(uo);
                }
                if (list.Count < 2) return;

                // 配列が前面→背面なのか背面→前面なのかは保証されていないので、
                // 「フォーカスされているウィンドウのコンテナが最前面のはず」を手掛かりに判定する。
                // 端のどちらでもなければ z 順の復元は諦める (誤った順序で並べ替えないため)。
                var focusedContainer = ContainerOf(_focused);
                if (focusedContainer == null) return;

                if (list[0] == focusedContainer)
                {
                    _containersFrontToBack.AddRange(list);
                }
                else if (list[list.Count - 1] == focusedContainer)
                {
                    list.Reverse();
                    _containersFrontToBack.AddRange(list);
                }
            }
            catch { }
        }

        static UnityEngine.Object ContainerOf(EditorWindow w)
        {
            if (w == null || s_HostViewField == null || s_ViewWindowProp == null) return null;
            try
            {
                var host = s_HostViewField.GetValue(w);
                if (host == null) return null;
                return s_ViewWindowProp.GetValue(host) as UnityEngine.Object;
            }
            catch { return null; }
        }

        // ---------------- 復元 ----------------
        /// <param name="snap">null なら復元せず winner を前面に出すだけ</param>
        /// <param name="winner">
        /// 最終的に前面に居てほしいウィンドウ。「それ以外」を開く前の状態に戻す。
        /// null なら前面化もフォーカス操作も行わない。
        /// </param>
        /// <param name="showWinner">
        /// winner に Show() も呼ぶか。ユーザーが明示的に選んだときだけ true。
        /// (未表示のウィンドウを勝手に開かないため)
        /// </param>
        public static void Restore(LayoutSnapshot snap, EditorWindow winner, bool showWinner)
        {
            if (snap != null)
            {
                snap.RestoreDockTabs(winner);
                snap.RestoreContainerOrder();
            }

            if (winner == null) return;
            if (showWinner) winner.Show(); // 最小化・背面のフローティングを前面へ
            winner.Focus();                // ドック済みならタブを前面に切り替え
        }

        /// <param name="keep">このウィンドウが乗っているドックのタブ選択は戻さない</param>
        void RestoreDockTabs(EditorWindow keep)
        {
            if (s_SelectedProp == null) return;

            var keepDock = DockAreaOf(keep);
            foreach (var kv in _dockTabs)
            {
                if (kv.Key == null) continue;                    // 既に閉じられた DockArea
                if (keepDock != null && kv.Key == keepDock) continue;
                try
                {
                    // タブ数が減っていると範囲外になり得るので、変化が無ければ触らない
                    if ((int)s_SelectedProp.GetValue(kv.Key) == kv.Value) continue;
                    s_SelectedProp.SetValue(kv.Key, kv.Value);
                }
                catch { }
            }
        }

        static UnityEngine.Object DockAreaOf(EditorWindow w)
        {
            if (w == null || s_HostViewField == null || s_DockAreaType == null) return null;
            try
            {
                var host = s_HostViewField.GetValue(w);
                if (host == null || !s_DockAreaType.IsInstanceOfType(host)) return null;
                return host as UnityEngine.Object;
            }
            catch { return null; }
        }

        void RestoreContainerOrder()
        {
            if (s_MoveBehindOf == null || _containersFrontToBack.Count < 2) return;
            try
            {
                // 前面から順に「1つ前のウィンドウの背面」へ送っていくと元の重なりに戻る
                UnityEngine.Object front = null;
                foreach (var c in _containersFrontToBack)
                {
                    if (c == null) continue; // 既に閉じられたコンテナ
                    if (front != null) s_MoveBehindOf.Invoke(c, new object[] { front });
                    front = c;
                }
            }
            catch { }
        }
    }
}
