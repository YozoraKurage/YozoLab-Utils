using UnityEditor;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// MeshBakeGroupのインスペクタ。
    /// 既定のプロパティに加えて、Rendererを再帰収集するドラッグ＆ドロップ領域を提供する。
    /// </summary>
    [CustomEditor(typeof(MeshBakeGroup))]
    public class MeshBakeGroupEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            serializedObject.Update();
            MeshBakeAssemblyEditor.DrawDropArea(serializedObject.FindProperty("renderers"));
            serializedObject.ApplyModifiedProperties();

            var group = (MeshBakeGroup)target;
            if (group.GetComponentInParent<MeshBakeAssembly>() == null)
            {
                EditorGUILayout.HelpBox(
                    "このグループはMeshBakeAssemblyの子階層にありません。" +
                    "ベイク対象にするには、MeshBakeAssemblyを持つGameObjectの配下に置いてください。",
                    MessageType.Warning);
            }
        }
    }
}
