using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// Unity FBX Exporter が入っていないときだけ現れる案内。
    ///
    /// このアセンブリは asmdef の defineConstraints が "!YOZOLAB_HAS_FBXEXPORTER" なので、
    /// com.unity.formats.fbx が入っていない環境でだけコンパイルされる。逆に入っていれば
    /// 本体側がコンパイルされ、こちらは丸ごと消える。同じメニュー項目が二重に出ることはない。
    ///
    /// 案内が要るのは、本体が「コンパイルされない」形で無効化されるため。以前は
    /// メニューに何も出ず、なぜ使えないのかを知る手がかりがどこにも無かった。
    /// </summary>
    internal sealed class MissingFbxExporterNotice : EditorWindow
    {
        private const string PackageId = "com.unity.formats.fbx";
        private const string ToolName = "FBX Animation Baker";

        private static AddRequest addRequest;

        [MenuItem("YozoLab/" + ToolName)]
        private static void Open()
        {
            GetWindow<MissingFbxExporterNotice>(true, ToolName).minSize = new Vector2(420f, 190f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(ToolName, EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                $"このツールは Unity FBX Exporter ({PackageId}) を必要とします。\n"
                + "入っていないあいだはコンパイルされないため、ウィンドウを開けません。\n\n"
                + $"This tool requires the Unity FBX Exporter ({PackageId}).\n"
                + "It is not compiled while the package is missing, so the window cannot open.",
                MessageType.Info);

            EditorGUILayout.Space();

            if (addRequest != null && !addRequest.IsCompleted)
            {
                EditorGUILayout.LabelField("インストール中… / Installing…", EditorStyles.miniLabel);
                Repaint();
                return;
            }

            if (addRequest != null && addRequest.Status == StatusCode.Failure)
            {
                EditorGUILayout.HelpBox(
                    "インストールに失敗しました。Package Manager から手動で追加してください。\n"
                    + $"Install failed: {addRequest.Error?.message}",
                    MessageType.Error);
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("インストール / Install", GUILayout.Height(24)))
            {
                // 追加が終わるとパッケージ解決とリコンパイルが走り、このアセンブリは
                // 制約を満たさなくなって消える。次からは本体のウィンドウが開く。
                addRequest = Client.Add(PackageId);
            }

            if (GUILayout.Button("Package Manager を開く / Open", GUILayout.Height(24)))
            {
                UnityEditor.PackageManager.UI.Window.Open(PackageId);
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
