using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// 保存してある鎖（<see cref="ChainStore"/>）をアバターごとに一覧し、編集する。
    ///
    /// ハンドルの表示と操作は <see cref="ChainHandlesScene"/> が受け持つので、
    /// このウィンドウは閉じていてもよい。ここは鎖の中身（始点・終点・分割数）と
    /// 表示の ON/OFF、動かし方の設定を触るための場所。
    /// </summary>
    internal sealed class ChainHandlesWindow : EditorWindow
    {
        private const string MenuPath = "YozoLab/Chain Handles/鎖の一覧";
        private const string UndoName = ChainHandlesScene.UndoName;

        private Vector2 _scroll;
        private readonly List<Transform> _joints = new List<Transform>();
        private readonly List<int> _anchorIndices = new List<int>();

        private static ChainStore Store => ChainStore.instance;

        [MenuItem(MenuPath, false, 301)]
        private static void Open() => GetWindow<ChainHandlesWindow>("Chain Handles");

        private void OnEnable()
        {
            ChainStore.Changed += Repaint;
            EditorApplication.hierarchyChanged += Repaint;
        }

        private void OnDisable()
        {
            ChainStore.Changed -= Repaint;
            EditorApplication.hierarchyChanged -= Repaint;
        }

        private void OnGUI()
        {
            ChainStore store = Store;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                bool visible = GUILayout.Toggle(store.gizmosVisible, "ギズモを表示", EditorStyles.toolbarButton, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck()) ChainHandlesScene.SetEnabled(visible);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("＋ 選択から鎖を追加", EditorStyles.toolbarButton))
                {
                    Rect anchor = GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect());
                    ChainPickerWindow.StartFromSelection(anchor, ChainHandlesScene.StartChain);
                    GUIUtility.ExitGUI();
                }
            }

            using (var scope = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scope.scrollPosition;

                if (store.avatars.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "まだ鎖がありません。\n" +
                        "階層ウィンドウでボーンを右クリック →「YozoLab/Chain Handles で動かす」、\n" +
                        "または選んでから上の「＋ 選択から鎖を追加」で作れます。\n" +
                        "一本の並びにあるボーンを 3 つ以上選んで作ると、選んだボーンにハンドルが付きます。",
                        MessageType.Info);
                }

                // シーンにあるアバターを先に並べる。
                var order = new List<ChainStore.AvatarEntry>(store.avatars);
                order.Sort((a, b) =>
                    (AvatarRoots.Resolve(a.key) == null).CompareTo(AvatarRoots.Resolve(b.key) == null));

                foreach (ChainStore.AvatarEntry avatar in order)
                {
                    if (!DrawAvatar(avatar)) break;
                }
            }

            EditorGUILayout.Space();
            DrawOptions();

            EditorGUILayout.HelpBox(
                "・水色の点をドラッグ: その関節を掴んで動かす　・点をクリックで選択: 移動とロール\n" +
                "・回転ツール（E）: 選んだ点の関節だけを回す　・灰色の根元の点: ロールだけ\n" +
                "変えるのは各ボーンの回転だけで、長さとスケールは変わりません。\n" +
                "鎖はこのプロジェクトの UserSettings に保存され、アバターには何も書き込みません。",
                MessageType.None);
        }

        private void DrawOptions()
        {
            ChainStore store = Store;
            EditorGUI.BeginChangeCheck();

            var mode = (SolveMode)EditorGUILayout.Popup(
                new GUIContent("動かし方"),
                (int)store.mode,
                new[]
                {
                    new GUIContent("IK（掴んだ関節を届かせる・親も曲がる）"),
                    new GUIContent("曲線（ハンドルを通る曲線に沿わせる・他のハンドルは留まる）"),
                });

            float bias = store.ikBias;
            bool carry = store.carryDownstream;
            bool keepRest = store.keepRest;
            if (mode == SolveMode.IK)
            {
                // 表示は「先端寄りほど右」にしたいので、bias の符号を反転して見せる。
                bias = -EditorGUILayout.Slider(
                    new GUIContent("曲がりの配分",
                        "どの関節を多く曲げるか。0: 根元寄り / 0.7: ほぼ均等 / 1.5: 先端寄り"),
                    -store.ikBias, 0f, 1.5f);
                keepRest = EditorGUILayout.ToggleLeft(
                    new GUIContent("掴んだ点より先をなるべく留める",
                        "ON: 途中の点を掴んだとき、先の点は元の位置に、先端は元の向きに留まろうとする。" +
                        "OFF: 先の部分は形を保ったまま一緒に運ばれる。"),
                    store.keepRest);
            }
            else
            {
                carry = EditorGUILayout.ToggleLeft(
                    new GUIContent("先のハンドルも一緒に動かす",
                        "ON: 掴んだ点より先の点も同じだけ動く。OFF: 他の点は留まる。"),
                    store.carryDownstream);
            }

            float scale = EditorGUILayout.Slider("ハンドルの大きさ", store.handleScale, 0.25f, 4f);

            if (EditorGUI.EndChangeCheck())
            {
                if (mode != store.mode) ChainHandlesScene.EndDrag();
                store.Modify(UndoName, () =>
                {
                    store.mode = mode;
                    store.ikBias = bias;
                    store.carryDownstream = carry;
                    store.keepRest = keepRest;
                    store.handleScale = scale;
                });
            }
        }

        // ---------------------------------------------------------------
        // アバター 1 体分
        // ---------------------------------------------------------------

        /// <summary>アバター 1 体分の欄。一覧が変わったら false（描画を打ち切る）。</summary>
        private bool DrawAvatar(ChainStore.AvatarEntry avatar)
        {
            ChainStore store = Store;
            Transform root = AvatarRoots.Resolve(avatar.key);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                bool expanded = EditorGUILayout.Foldout(avatar.expanded, GUIContent.none, true);
                if (EditorGUI.EndChangeCheck()) store.Set(() => avatar.expanded = expanded);

                EditorGUI.BeginChangeCheck();
                bool visible = EditorGUILayout.ToggleLeft(
                    new GUIContent(root != null ? root.name : avatar.name,
                        "このアバターの鎖のハンドルを表示する"),
                    avatar.visible, EditorStyles.boldLabel);
                if (EditorGUI.EndChangeCheck()) store.Set(() => avatar.visible = visible);

                GUILayout.FlexibleSpace();
                if (root == null)
                    GUILayout.Label("シーンに無い", EditorStyles.miniLabel);
                else if (GUILayout.Button("選択", EditorStyles.miniButton, GUILayout.Width(40f)))
                    EditorGUIUtility.PingObject(root.gameObject);

                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                {
                    ChainHandlesScene.EndDrag();
                    ChainHandlesScene.Select(null, -1);
                    store.Modify(UndoName, () => store.avatars.Remove(avatar));
                    return false;
                }
            }

            if (!avatar.expanded) return true;

            using (new EditorGUI.IndentLevelScope())
            {
                if (root == null)
                {
                    EditorGUILayout.LabelField(
                        $"鎖 {avatar.chains.Count} 本を保存中。アバターのあるシーンを開くと出ます。",
                        EditorStyles.wordWrappedMiniLabel);
                    return true;
                }

                // 名前を変えられていたら、次にシーンに無いときのために覚え直す。
                if (avatar.name != root.name) store.Set(() => avatar.name = root.name);

                for (int i = 0; i < avatar.chains.Count; i++)
                {
                    if (!DrawChain(avatar, root, i)) return false;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15f);
                    if (GUILayout.Button("＋ 空の鎖", EditorStyles.miniButton, GUILayout.Width(70f)))
                    {
                        store.Modify(UndoName, () => avatar.chains.Add(new ChainStore.Chain { start = "", end = "" }));
                    }
                }
            }
            EditorGUILayout.Space(4f);
            return true;
        }

        // ---------------------------------------------------------------
        // 鎖 1 本分
        // ---------------------------------------------------------------

        /// <summary>鎖 1 本分の欄。一覧が変わったら false（描画を打ち切る）。</summary>
        private bool DrawChain(ChainStore.AvatarEntry avatar, Transform root, int index)
        {
            ChainStore store = Store;
            ChainStore.Chain chain = avatar.chains[index];

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool built = ChainHandlesScene.TryBuild(root, chain, _joints, out string error);
                string title = built ? $"{_joints[0].name} → {_joints[_joints.Count - 1].name}" : $"鎖 {index + 1}";

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool visible = EditorGUILayout.ToggleLeft(title, chain.visible, EditorStyles.boldLabel);
                    if (EditorGUI.EndChangeCheck()) store.Set(() => chain.visible = visible);

                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                    {
                        ChainHandlesScene.EndDrag();
                        ChainHandlesScene.Select(null, -1);
                        store.Modify(UndoName, () =>
                        {
                            avatar.chains.RemoveAt(index);
                            if (avatar.chains.Count == 0) store.avatars.Remove(avatar);
                        });
                        return false;
                    }
                }

                // 始点・終点。アバターの外は選べない（アバターごとに保存しているので）。
                EditorGUI.BeginChangeCheck();
                var start = (Transform)EditorGUILayout.ObjectField("始点", AvatarRoots.Find(root, chain.start), typeof(Transform), true);
                var end = (Transform)EditorGUILayout.ObjectField("終点", AvatarRoots.Find(root, chain.end), typeof(Transform), true);
                if (EditorGUI.EndChangeCheck())
                {
                    string startPath = start != null ? AvatarRoots.PathFrom(root, start) : "";
                    string endPath = end != null ? AvatarRoots.PathFrom(root, end) : "";
                    if (startPath == null || endPath == null)
                    {
                        Debug.LogWarning($"[Chain Handles] {root.name} の中のオブジェクトを指定してください");
                    }
                    else
                    {
                        // 始点・終点を手で変えたら、選んだボーンへの指定はもう当てにならない。
                        store.Modify(UndoName, () =>
                        {
                            chain.start = startPath;
                            chain.end = endPath;
                            chain.anchors.Clear();
                        });
                        built = ChainHandlesScene.TryBuild(root, chain, _joints, out error);
                    }
                }

                bool anchored = built && ChainHandlesScene.TryAnchorIndices(root, chain, _joints, _anchorIndices);

                if (chain.anchors.Count > 0)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("ハンドル", $"選んだボーン {chain.anchors.Count} 点");
                        if (GUILayout.Button("等分に戻す", GUILayout.Width(80f)))
                            store.Modify(UndoName, () => chain.anchors.Clear());
                    }
                    if (built && !anchored)
                    {
                        EditorGUILayout.HelpBox(
                            "ハンドルを置くボーンが鎖から外れています（名前や階層が変わったなど）。" +
                            "分割数で選んだ関節に置きます。",
                            MessageType.Warning);
                    }
                }

                if (!anchored)
                {
                    // 分割数はボーンの本数まで。それ以上に割っても同じ関節に重なるだけ。
                    int bones = built ? Mathf.Max(1, _joints.Count - 1) : ChainHandlesScene.MaxDivisions;
                    int max = Mathf.Min(ChainHandlesScene.MaxDivisions, bones);

                    EditorGUI.BeginChangeCheck();
                    int divisions = EditorGUILayout.IntSlider(
                        new GUIContent("分割数", "ハンドルの間隔。ハンドルは根元を含めて分割数 + 1 個で、関節の上に置く。"),
                        Mathf.Clamp(chain.divisions, 1, max), 1, max);
                    if (EditorGUI.EndChangeCheck()) store.Modify(UndoName, () => chain.divisions = divisions);
                }

                if (!built)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Warning);
                    return true;
                }

                EditorGUILayout.LabelField($"関節 {_joints.Count} 個 / 全長 {ChainCandidates.ChainLength(_joints):0.###}");

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
    }
}
