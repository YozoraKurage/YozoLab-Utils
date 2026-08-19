// この機能は Harmony（0Harmony.dll）に依存します。
// Harmony を同梱する VRCSDK（com.vrchat.base）がある環境でだけコンパイルされ、
// 判定は asmdef の versionDefines に任せています（手動設定は不要）。
// Harmony が無い環境ではこのファイルは空になり、コンパイルは通ります。
//
// EditorPatcher（net.nekobako.editor-patcher）がある環境では追加の経路を引きます。
// こちらも判定は asmdef の versionDefines（YOZOLAB_ANIMTOOLS_EDITORPATCHER）で、
// 相手のアセンブリは参照しません。触るのは Unity の API だけです。
#if YOZOLAB_ANIMTOOLS_HARMONY
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace YozoLab.AnimTools
{
    /// <summary>
    /// SkinnedMeshRenderer インスペクタでブレンドシェイプ名をダブルクリックすると、
    /// Animation ウィンドウの一致するプロパティ行を選択してスクロール・点滅させる機能。
    ///
    /// 目的：表情を打ち込む作業で、インスペクタでシェイプを探す動作と Animation ウィンドウで
    /// 該当プロパティを探す動作が二重になるのを解消する。
    ///
    /// **一致行が既にある場合しか動かない。** まだキーを持たないシェイプでは何も起きない。
    /// これは仕様であって故障ではない。ダブルクリックという軽い操作に Clip の書き換えを
    /// 伴わせないため、「既存のキーへ飛ぶ」と「新しいプロパティを追加する」は分けている。
    ///
    /// 実装は二段。掴む側は <c>SkinnedMeshRendererEditor.OnBlendShapeUI</c> を Prefix/Postfix で
    /// 囲み、対象 SMR とそのフレームのダブルクリック位置を記録する。行の矩形は、素の Unity なら
    /// <c>EditorGUILayout.Slider</c> の Postfix と <c>GUILayoutUtility.GetLastRect()</c> から、
    /// EditorPatcher 環境なら <c>EditorGUI.Slider</c>（12引数）の Postfix から引数で直接得る。
    ///
    /// 光らせる側は <c>AnimationWindowState.hierarchyData.GetRows()</c> を走査して binding が
    /// 一致する行を探し、<c>SelectHierarchyItem</c> と <c>TreeViewController.Frame</c> を呼ぶ。
    ///
    /// EditorPatcher との共存については ADR 0002 を参照。要点は、あちらが Unity 本来の
    /// ブレンドシェイプ UI を丸ごと差し替え、かつ行内の MouseDown を握り潰すため、
    /// あちらより後ろで待っていてもクリックは観測できない、ということ。
    ///
    /// 内部 API 依存のため、失敗してもインスペクタを壊さないよう全て握りつぶす。
    /// </summary>
    [InitializeOnLoad]
    internal static class BlendShapeAnimationLink
    {
        private const string HarmonyId = "net.yozolab.animtools.blendshapelink";
        private const string BlendShapePrefix = "blendShape.";

        // OnBlendShapeUI の中だけで有効な、そのフレーム分の状態。
        // 位置をここに持たないのが要点。理由は HandleRow のコメントを参照。
        private static bool drawingBlendShapes;
        private static SkinnedMeshRenderer target;
        private static bool pendingDoubleClick;

        // 内部 API 読み取り用リフレクションキャッシュ
        private static FieldInfo animEditorField;    // AnimationWindow.m_AnimEditor
        private static PropertyInfo stateProp;       // AnimEditor.state
        private static FieldInfo hierarchyField;     // AnimEditor.m_Hierarchy
        private static FieldInfo treeViewField;      // AnimationWindowHierarchy.m_TreeView
        private static MethodInfo frameMethod;       // TreeViewController.Frame(int, bool, bool)
        private static FieldInfo hierarchyDataField; // AnimationWindowState.hierarchyData
        private static PropertyInfo rootGameObjectProp; // AnimationWindowState.activeRootGameObject
        private static MethodInfo selectHierarchyItem;  // AnimationWindowState.SelectHierarchyItem
        private static MethodInfo getRowsMethod;     // AnimationWindowHierarchyDataSource.GetRows
        private static FieldInfo nodeBindingField;   // AnimationWindowHierarchyNode.binding

        static BlendShapeAnimationLink()
        {
            TryApplyPatch();
        }

        // ---------------------------------------------------------------
        // Harmony パッチ適用
        // ---------------------------------------------------------------

        private static void TryApplyPatch()
        {
            try
            {
                Assembly editorAsm = typeof(Editor).Assembly;
                BindingFlags sf = BindingFlags.Static | BindingFlags.NonPublic;

                Type smrEditor = editorAsm.GetType("UnityEditor.SkinnedMeshRendererEditor");
                MethodInfo onBlendShapeUI = smrEditor?.GetMethod("OnBlendShapeUI",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (onBlendShapeUI == null)
                {
                    Debug.LogWarning("[BlendShapeLink] SkinnedMeshRendererEditor.OnBlendShapeUI が見つかりませんでした（Unity のバージョン差異）。機能を無効化します。");
                    return;
                }

                var harmony = new Harmony(HarmonyId);

                // Prefix は void かつ引数を __instance だけに保つこと。こうしておくと Harmony の
                // PrefixAffectsOriginal が false と判定し、他パッケージの Prefix が false を
                // 返して本体をスキップさせた後でも、この Prefix は実行される。
                // EditorPatcher が Unity の UI を丸ごと差し替える環境ではこれが生命線になる。
                harmony.Patch(
                    onBlendShapeUI,
                    prefix: new HarmonyMethod(typeof(BlendShapeAnimationLink).GetMethod(nameof(BlendShapeUIPrefix), sf)),
                    postfix: new HarmonyMethod(typeof(BlendShapeAnimationLink).GetMethod(nameof(BlendShapeUIPostfix), sf)));

                PatchStockSlider(harmony, sf);
#if YOZOLAB_ANIMTOOLS_EDITORPATCHER
                PatchEditorPatcherSlider(harmony, sf);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BlendShapeLink] パッチ適用に失敗しました（機能は無効のまま）: {e.Message}");
            }
        }

        /// <summary>素の Unity の経路。SkinnedMeshRendererEditor が呼ぶ内部 overload。</summary>
        private static void PatchStockSlider(Harmony harmony, BindingFlags sf)
        {
            try
            {
                MethodInfo slider = AccessTools.Method(typeof(EditorGUILayout), "Slider", new[]
                {
                    typeof(SerializedProperty), typeof(float), typeof(float),
                    typeof(float), typeof(float), typeof(GUIContent), typeof(GUILayoutOption[]),
                });
                if (slider == null) return;

                harmony.Patch(
                    slider,
                    postfix: new HarmonyMethod(typeof(BlendShapeAnimationLink).GetMethod(nameof(StockSliderPostfix), sf)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BlendShapeLink] 標準 UI 側の設置に失敗しました: {e.Message}");
            }
        }

#if YOZOLAB_ANIMTOOLS_EDITORPATCHER
        /// <summary>
        /// EditorPatcher の経路。あちらの BlendShapesDrawer が行の描画に使っている
        /// Unity 内部の overload を押さえる。あちらの型には触らない。
        /// この呼び出しは、あちらが MouseDown を握り潰す箇所より前に来る。
        /// </summary>
        private static void PatchEditorPatcherSlider(Harmony harmony, BindingFlags sf)
        {
            try
            {
                MethodInfo slider = AccessTools.Method(typeof(EditorGUI), "Slider", new[]
                {
                    typeof(Rect), typeof(GUIContent), typeof(float), typeof(float), typeof(float),
                    typeof(float), typeof(float), typeof(GUIStyle), typeof(GUIStyle), typeof(GUIStyle),
                    typeof(Texture2D), typeof(GUIStyle),
                });
                if (slider == null) return;

                harmony.Patch(
                    slider,
                    postfix: new HarmonyMethod(typeof(BlendShapeAnimationLink).GetMethod(nameof(PatchedSliderPostfix), sf)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BlendShapeLink] EditorPatcher 側の設置に失敗しました: {e.Message}");
            }
        }
#endif

        // ---------------------------------------------------------------
        // 掴む側
        // ---------------------------------------------------------------

        /// <summary>
        /// ブレンドシェイプ欄の描画に入る。ダブルクリックが起きたという事実だけをここで拾う。
        /// Slider は自分が処理したイベントを消費してしまうので、行を描く場所では
        /// Event.current が Used になっており、種別からは判定できない。
        /// </summary>
        private static void BlendShapeUIPrefix(object __instance)
        {
            try
            {
                drawingBlendShapes = true;
                target = (__instance as Editor)?.target as SkinnedMeshRenderer;

                Event e = Event.current;
                pendingDoubleClick = target != null && e != null
                    && e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2;
            }
            catch
            {
                drawingBlendShapes = false;
                pendingDoubleClick = false;
            }
        }

        private static void BlendShapeUIPostfix()
        {
            drawingBlendShapes = false;
            target = null;
            pendingDoubleClick = false;
        }

        /// <summary>素の Unity の行。矩形は直前に描かれたものとして取り出す。</summary>
        private static void StockSliderPostfix(GUIContent label)
        {
            if (!drawingBlendShapes || !pendingDoubleClick) return;
            try
            {
                HandleRow(GUILayoutUtility.GetLastRect(), label);
            }
            catch
            {
                // 描画中の例外でインスペクタを壊さないよう握りつぶす。
            }
        }

#if YOZOLAB_ANIMTOOLS_EDITORPATCHER
        /// <summary>EditorPatcher の行。矩形もラベルも引数で渡ってくる。</summary>
        private static void PatchedSliderPostfix(Rect position, GUIContent label)
        {
            if (!drawingBlendShapes || !pendingDoubleClick) return;
            try
            {
                HandleRow(position, label);
            }
            catch
            {
                // 同上。
            }
        }
#endif

        /// <summary>
        /// 行の矩形のうち名前が描かれている側（先頭 labelWidth 分）にダブルクリックが
        /// 入っていれば、Animation ウィンドウ側へ飛ばす。
        ///
        /// マウス位置は必ず「行を描いている今ここ」で読むこと。OnBlendShapeUI の入口で
        /// 記録した位置を持ち回ってはならない。EditorPatcher の行は TreeView が
        /// useScrollView = false で描くため GUI.BeginClip の内側にあり
        /// （TreeViewController.OnGUI）、クリップの内と外では Event.current.mousePosition が
        /// 平行移動する。入口で取った座標を持ち込むと、リストの左上位置のぶんだけ
        /// ずれた行に当たる（行高 24px に対し数行ぶん、スクロール量で変動する）。
        /// row も Event.current もこの場所で読めば、常に同じ座標系になる。
        /// </summary>
        private static void HandleRow(Rect row, GUIContent label)
        {
            if (label == null || string.IsNullOrEmpty(label.text)) return;

            Event e = Event.current;
            if (e == null) return;

            var labelRect = new Rect(row.x, row.y, Mathf.Min(EditorGUIUtility.labelWidth, row.width), row.height);
            if (!labelRect.Contains(e.mousePosition)) return;

            pendingDoubleClick = false; // 一度きり。以降の行では判定しない
            TryReveal(target, label.text);
        }

        // ---------------------------------------------------------------
        // 光らせる側
        // ---------------------------------------------------------------

        /// <summary>
        /// 開いている Animation ウィンドウのうち、この SMR を配下に持つものを探し、
        /// 該当プロパティの行を選択して見える位置まで送る。行が無ければ何もしない。
        /// </summary>
        private static void TryReveal(SkinnedMeshRenderer renderer, string shapeName)
        {
            if (renderer == null) return;

            try
            {
                Type winType = typeof(Editor).Assembly.GetType("UnityEditor.AnimationWindow");
                if (winType == null) return;

                foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(winType))
                {
                    var window = obj as EditorWindow;
                    if (window == null) continue;

                    object animEditor = GetAnimEditor(window);
                    object state = GetState(animEditor);
                    if (state == null) continue;

                    var root = GetRootGameObject(state);
                    if (root == null) continue;

                    if (!TryGetRelativePath(renderer.transform, root.transform, out string path)) continue;

                    var binding = new EditorCurveBinding
                    {
                        path = path,
                        type = typeof(SkinnedMeshRenderer),
                        propertyName = BlendShapePrefix + shapeName,
                    };

                    if (!TryFindRow(state, binding, out int nodeId)) continue;

                    SelectRow(state, nodeId);
                    FrameRow(animEditor, nodeId);
                    window.Repaint();
                    return;
                }
            }
            catch
            {
                // 内部 API 側の想定外は黙って諦める（何も起きないだけ）。
            }
        }

        /// <summary>
        /// root から見た相対パスを求める。root 配下でなければ false。
        /// AnimationUtility.CalculateTransformPath は配下であることを前提にしているので、
        /// ここで先に辿り着けるかを確かめる。
        /// </summary>
        private static bool TryGetRelativePath(Transform child, Transform root, out string path)
        {
            path = string.Empty;
            for (Transform t = child; t != null; t = t.parent)
            {
                if (t == root) return true;
                path = (path.Length == 0) ? t.name : t.name + "/" + path;
            }
            path = string.Empty;
            return false;
        }

        /// <summary>階層の行を走査して binding が一致するものの id を返す。</summary>
        private static bool TryFindRow(object state, EditorCurveBinding binding, out int nodeId)
        {
            nodeId = 0;

            if (hierarchyDataField == null || hierarchyDataField.DeclaringType?.IsInstanceOfType(state) == false)
                hierarchyDataField = state.GetType().GetField("hierarchyData",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object hierarchyData = hierarchyDataField?.GetValue(state);
            if (hierarchyData == null) return false;

            if (getRowsMethod == null || getRowsMethod.DeclaringType?.IsInstanceOfType(hierarchyData) == false)
                getRowsMethod = hierarchyData.GetType().GetMethod("GetRows",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
            if (!(getRowsMethod?.Invoke(hierarchyData, null) is IList rows)) return false;

            foreach (object row in rows)
            {
                if (row == null) continue;

                if (nodeBindingField == null || nodeBindingField.DeclaringType?.IsInstanceOfType(row) == false)
                    nodeBindingField = row.GetType().GetField("binding",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (!(nodeBindingField?.GetValue(row) is EditorCurveBinding rowBinding)) continue;

                if (rowBinding.type != binding.type) continue;
                if (rowBinding.path != binding.path) continue;
                if (rowBinding.propertyName != binding.propertyName) continue;

                PropertyInfo idProp = row.GetType().GetProperty("id",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (!(idProp?.GetValue(row) is int id)) continue;

                nodeId = id;
                return true;
            }
            return false;
        }

        private static void SelectRow(object state, int nodeId)
        {
            if (selectHierarchyItem == null || selectHierarchyItem.DeclaringType?.IsInstanceOfType(state) == false)
                selectHierarchyItem = state.GetType().GetMethod("SelectHierarchyItem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(int), typeof(bool), typeof(bool) }, null);

            // additive: false（この行だけを選ぶ）、triggerSceneSelectionSync: false
            //（インスペクタで選んでいる対象を Animation ウィンドウ側から動かさない）。
            selectHierarchyItem?.Invoke(state, new object[] { nodeId, false, false });
        }

        /// <summary>行が画面外なら送って、点滅させる。</summary>
        private static void FrameRow(object animEditor, int nodeId)
        {
            if (animEditor == null) return;

            if (hierarchyField == null || hierarchyField.DeclaringType?.IsInstanceOfType(animEditor) == false)
                hierarchyField = animEditor.GetType().GetField("m_Hierarchy",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            object hierarchy = hierarchyField?.GetValue(animEditor);
            if (hierarchy == null) return;

            if (treeViewField == null || treeViewField.DeclaringType?.IsInstanceOfType(hierarchy) == false)
                treeViewField = hierarchy.GetType().GetField("m_TreeView",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            object treeView = treeViewField?.GetValue(hierarchy);
            if (treeView == null) return;

            if (frameMethod == null || frameMethod.DeclaringType?.IsInstanceOfType(treeView) == false)
                frameMethod = treeView.GetType().GetMethod("Frame",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(int), typeof(bool), typeof(bool) }, null);

            frameMethod?.Invoke(treeView, new object[] { nodeId, true, true });
        }

        // ---------------------------------------------------------------
        // AnimationWindow の内部へ辿る
        // ---------------------------------------------------------------

        private static object GetAnimEditor(EditorWindow window)
        {
            if (animEditorField == null || animEditorField.DeclaringType?.IsInstanceOfType(window) == false)
                animEditorField = window.GetType().GetField("m_AnimEditor",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            return animEditorField?.GetValue(window);
        }

        private static object GetState(object animEditor)
        {
            if (animEditor == null) return null;

            if (stateProp == null || stateProp.DeclaringType?.IsInstanceOfType(animEditor) == false)
                stateProp = animEditor.GetType().GetProperty("state",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return stateProp?.GetValue(animEditor);
        }

        private static GameObject GetRootGameObject(object state)
        {
            if (rootGameObjectProp == null || rootGameObjectProp.DeclaringType?.IsInstanceOfType(state) == false)
                rootGameObjectProp = state.GetType().GetProperty("activeRootGameObject",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return rootGameObjectProp?.GetValue(state) as GameObject;
        }
    }
}
#endif // YOZOLAB_ANIMTOOLS_HARMONY
