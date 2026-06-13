using UnityEditor;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// MeshBakeItemのインスペクタ。
    /// 既定のプロパティに加えて、Rendererを再帰収集するドラッグ＆ドロップ領域を提供する。
    /// </summary>
    [CustomEditor(typeof(MeshBakeItem))]
    public class MeshBakeItemEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            serializedObject.Update();
            MeshBakeGroupEditor.DrawDropArea(serializedObject.FindProperty("renderers"));
            serializedObject.ApplyModifiedProperties();

            var item = (MeshBakeItem)target;
            if (item.GetComponentInParent<MeshBakeGroup>() == null)
            {
                EditorGUILayout.HelpBox(
                    "このアイテムはMeshBakeGroupの子階層にありません。" +
                    "ベイク対象にするには、MeshBakeGroupを持つGameObjectの配下に置いてください。",
                    MessageType.Warning);
            }
        }
    }
}
