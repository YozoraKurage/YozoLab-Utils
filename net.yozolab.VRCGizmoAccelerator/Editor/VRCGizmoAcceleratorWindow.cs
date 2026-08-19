using UnityEngine;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 状態確認と ON/OFF のためのウィンドウ。
    /// パッチの状態と、代替パスが何をどれだけ描いているかがここで分かる。
    /// </summary>
    public class VRCGizmoAcceleratorWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem("YozoLab/VRC Gizmo Accelerator")]
        public static void Open()
        {
            var window = GetWindow<VRCGizmoAcceleratorWindow>("Gizmo Accelerator");
            window.minSize = new Vector2(380, 280);
            window.Show();
        }

        private double _lastRepaint;

        private void OnEnable()
        {
            EditorApplication.update += RepaintWhileVisible;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintWhileVisible;
        }

        // 統計を出しているので追従させるが、毎 tick 描き直すとこのウィンドウ自身が
        // IMGUI の負荷になる。10Hz で足りる。
        private void RepaintWhileVisible()
        {
            if (!PhysBoneGizmoPass.Active) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaint < 0.1) return;
            _lastRepaint = now;
            Repaint();
        }

        private void OnGUI()
        {
            var settings = VRCGizmoAcceleratorSettings.instance;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField(
                "PhysBone の SDK ギズモを Harmony で止め、選択に関連するものだけを\n"
                + "軽量な一括描画パスで描き直します。\n"
                + "このパッケージをコンパイルする設定にした時点で有効になります\n"
                + "（YozoLab Utils ウィンドウが入口。ここの OFF は再コンパイル無しの退避用）。",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

#if !YOZOLAB_VRCGIZMOACC_VRCSDK
            EditorGUILayout.HelpBox(
                "VRChat SDK（com.vrchat.base）が見つからないため、この機能は使えません。",
                MessageType.Warning);
            EditorGUILayout.EndScrollView();
#else
            EditorGUI.BeginChangeCheck();

            settings.enabled = EditorGUILayout.ToggleLeft(
                "有効にする", settings.enabled, EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!settings.enabled))
            {
                EditorGUI.indentLevel++;
                settings.drawUnselected = EditorGUILayout.ToggleLeft(
                    "選択していない PhysBone も描く（半透明・SDK 互換の見え方）",
                    settings.drawUnselected);
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck())
            {
                settings.SaveSettings();
                PhysBoneGizmoPass.Invalidate();
            }

            EditorGUILayout.Space();

            if (settings.enabled)
            {
                EditorGUILayout.LabelField("状態", EditorStyles.boldLabel);

                if (SdkGizmoSuppressor.Installed)
                {
                    EditorGUILayout.LabelField("SDK のギズモ: 停止中（Harmony パッチ）");
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "SDK のギズモを止められていません: "
                        + (SdkGizmoSuppressor.UnavailableReason ?? "（未適用）"),
                        MessageType.Warning);
                }

                EditorGUILayout.LabelField($"検知した PhysBone: {PhysBoneScanner.All.Count}");
                EditorGUILayout.LabelField($"代替パスの描画対象: {PhysBoneGizmoDriver.LastTargetCount}");
                EditorGUILayout.LabelField(
                    $"頂点数: {PhysBoneGizmoDriver.LastVertexCount:N0} / 組み立て {PhysBoneGizmoDriver.LastBuildMs:0.00}ms");
                EditorGUILayout.LabelField($"登録されている拡張: {PhysBoneGizmoPass.Extensions.Count}");

                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "インスペクタの Show Gizmos はそのまま効きます（表示の判断に使われます）。",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
#endif
        }
    }
}
