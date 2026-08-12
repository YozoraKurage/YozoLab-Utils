using UnityEditor;
using UnityEditor.SceneManagement;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 「シーンの中身が変わったかもしれない」合図を 1 つの版数にまとめる。
    ///
    /// ギズモの内容は、階層とコンポーネントの設定が同じなら毎フレーム同じものになる。
    /// そこで変化の合図を全部ここで拾い、拾えたときに版数を上げる。キャッシュ側は
    /// 版数が変わったかどうかだけを見ればよい。
    ///
    /// 取りこぼしたときのために、キャッシュ側には時間による保険も入れてある。
    /// </summary>
    [InitializeOnLoad]
    internal static class InvalidationVersion
    {
        /// <summary>変化があるたびに増える。</summary>
        internal static int Current { get; private set; } = 1;

        static InvalidationVersion()
        {
            // 構造の変化
            EditorApplication.hierarchyChanged += Bump;
            // プロパティ・Transform の変更（インスペクタ操作もシーンでの移動も含む）
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Undo.undoRedoPerformed += Bump;
            // 選択が変われば、ギズモを描く対象そのものが変わる
            Selection.selectionChanged += Bump;
            EditorApplication.playModeStateChanged += _ => Bump();
            EditorSceneManager.sceneOpened += (_, __) => Bump();
            EditorSceneManager.sceneClosed += _ => Bump();
            PrefabStage.prefabStageOpened += _ => Bump();
            PrefabStage.prefabStageClosing += _ => Bump();
            AssemblyReloadEvents.beforeAssemblyReload += Bump;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            // 何が変わったかまでは見ない。作り直しは十分安いので、
            // 「何か変わった」だけで捨ててしまうほうが取りこぼしが無い。
            if (stream.length > 0) Bump();
        }

        internal static void Bump()
        {
            Current++;
        }
    }
}
