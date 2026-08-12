using System.Linq;
using UnityEngine;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 状態確認と ON/OFF のためのウィンドウ。
    /// パッチが当たっているか、何を横取りできているかがここで分かる。
    /// </summary>
    public class VRCGizmoAcceleratorWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _showDiagnostics;

        [MenuItem("YozoLab/VRC Gizmo Accelerator")]
        public static void Open()
        {
            var window = GetWindow<VRCGizmoAcceleratorWindow>("Gizmo Accelerator");
            window.minSize = new Vector2(380, 320);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += RepaintWhileVisible;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintWhileVisible;
        }

        private double _lastRepaint;

        // 統計を出しているので追従させるが、毎 tick 描き直すとこのウィンドウ自身が
        // IMGUI の負荷になる。10Hz で足りる。
        private void RepaintWhileVisible()
        {
            if (!VRCGizmoPatcher.Installed) return;

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
                "VRChat SDK のギズモ描画を高速化します。\n"
                + "既定では何もしません（下のチェックを入れるまでパッチを当てません）。",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            settings.enabled = EditorGUILayout.ToggleLeft("有効にする", settings.enabled, EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!settings.enabled))
            {
                EditorGUI.indentLevel++;
                settings.physBone = EditorGUILayout.ToggleLeft("PhysBone / PhysBone Collider", settings.physBone);
                settings.contact = EditorGUILayout.ToggleLeft("Contact Sender / Receiver", settings.contact);
                settings.constraint = EditorGUILayout.ToggleLeft("Constraint", settings.constraint);
                settings.avatarDescriptor = EditorGUILayout.ToggleLeft(
                    "Avatar Descriptor のコライダー", settings.avatarDescriptor);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
                settings.cacheBoneInit = EditorGUILayout.ToggleLeft(
                    "ボーン構造の作り直しを省く", settings.cacheBoneInit);
                EditorGUILayout.LabelField(
                    "　変化が無い間は PhysBone のボーン構造を作り直しません（CPU 側で最も効く項目）",
                    EditorStyles.miniLabel);


                settings.drawMode = (GizmoDrawMode)EditorGUILayout.EnumPopup("描画方式", settings.drawMode);
                EditorGUILayout.LabelField(
                    "　CommandBuffer（既定）: カメラに積む。Unity のギズモにも即時描画にも依存しない\n" +
                    "　GizmoLines: Unity のギズモレンダラに渡す（塗りは単色）\n" +
                    "　Immediate: GL で即時描画。IMGUI の描画分断が起きる",
                    EditorStyles.miniLabel, GUILayout.Height(38));

                settings.combineDrawCalls = EditorGUILayout.ToggleLeft(
                    "1 リペイントにまとめて発行する", settings.combineDrawCalls);
                EditorGUILayout.LabelField(
                    "　コンポーネントごとの発行を 1 回に束ねます（表示は 1 フレーム遅れます）",
                    EditorStyles.miniLabel);

                settings.cacheGeometry = EditorGUILayout.ToggleLeft(
                    "描画結果を使い回す", settings.cacheGeometry);
                EditorGUILayout.LabelField(
                    "　変化が無い間は SDK の描画処理ごと飛ばし、前回の頂点をそのまま描きます（メモリと引き換え）",
                    EditorStyles.miniLabel);

                settings.profilerMarkers = EditorGUILayout.ToggleLeft(
                    "プロファイラにマーカーを出す", settings.profilerMarkers);
                EditorGUILayout.LabelField(
                    "　\"YozoLab ...\" という名前で、どの入口が何 ms 使っているかが階層に出ます",
                    EditorStyles.miniLabel);

                settings.interceptUnityHandles = EditorGUILayout.ToggleLeft(
                    "Unity の Handles も横取りする（実験的）", settings.interceptUnityHandles);
                EditorGUILayout.LabelField(
                    "　SetPass はさらに減りますが、Unity がネイティブでやっている円弧の分割を\n" +
                    "　C# で肩代わりすることになります。Avatar Descriptor のコライダーはこちら側です",
                    EditorStyles.miniLabel, GUILayout.Height(26));

                settings.skipNonDrawingEvents = EditorGUILayout.ToggleLeft(
                    "描画イベント以外の描画呼び出しを捨てる", settings.skipNonDrawingEvents);
                EditorGUILayout.LabelField(
                    "　Layout / MouseMove では何も表示されないので、その分の処理を省きます（CPU 側）",
                    EditorStyles.miniLabel);

            }
            if (EditorGUI.EndChangeCheck())
            {
                settings.SaveSettings();
                VRCGizmoPatcher.Reinstall();
            }

            EditorGUILayout.Space();
            DrawStatus();
            EditorGUILayout.Space();
            DrawStats();
            EditorGUILayout.Space();
            DrawImmediateDrawDiagnostics();
            EditorGUILayout.Space();
            DrawDiagnostics();

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("状態", EditorStyles.boldLabel);

            if (!HarmonyBridge.Available)
            {
                EditorGUILayout.HelpBox(
                    $"Harmony が使えないため何もしていません。\n{HarmonyBridge.UnavailableReason}",
                    MessageType.Warning);
                return;
            }

            if (!VRCGizmoPatcher.Installed)
            {
                EditorGUILayout.HelpBox("パッチは当たっていません（元の描画のままです）", MessageType.Info);
            }
            else
            {
                var patched = VRCGizmoPatcher.AllTargets.Count(t => t.patched);
                EditorGUILayout.HelpBox(
                    $"稼働中: プリミティブ {VRCGizmoPatcher.PatchedPrimitiveCount} 個 / ギズモ入口 {patched} 個を横取りしています",
                    MessageType.Info);
            }

            EditorGUILayout.LabelField("Harmony", HarmonyBridge.HarmonyVersion);

            foreach (var target in VRCGizmoPatcher.AllTargets)
            {
                var state = target.method == null ? "見つからない"
                    : target.patched ? "適用中"
                    : target.skipReason ?? "未適用";
                EditorGUILayout.LabelField($"　{target.label}", state);
            }
        }

        private void DrawStats()
        {
            EditorGUILayout.LabelField("直近の 1 区間", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("　まとめた図形", $"{GizmoBatch.LastPrimitiveCount}");
            EditorGUILayout.LabelField("　頂点", $"{GizmoBatch.LastVertexCount}");
            EditorGUILayout.LabelField("　ドローコール", $"{GizmoBatch.LastDrawCallCount}");
            EditorGUILayout.LabelField("　SetPass", $"{GizmoBatch.LastSetPassCount}");

            int hits = PhysBoneInitCache.Hits, misses = PhysBoneInitCache.Misses;
            if (hits + misses > 0)
            {
                EditorGUILayout.LabelField("　構造の再構築を省いた回数",
                    $"{hits} / {hits + misses}（{100f * hits / (hits + misses):0.#}%）");
            }

            int gh = GizmoGeometryCache.Hits, gm = GizmoGeometryCache.Misses;
            if (gh + gm > 0)
            {
                EditorGUILayout.LabelField("　描画そのものを省いた回数",
                    $"{gh} / {gh + gm}（{100f * gh / (gh + gm):0.#}%）");

                // 頂点 1 つあたり Vector3 12B + Color 16B。目安として出す。
                float mb = GizmoGeometryCache.CachedVertices * 28f / (1024f * 1024f);
                EditorGUILayout.LabelField("　使い回している頂点",
                    $"{GizmoGeometryCache.CachedVertices:N0}（{GizmoGeometryCache.CachedComponents} 個 / 約 {mb:0.#} MB）");
            }

            if (GizmoBatch.LastDrawCallCount > 0)
            {
                // 元の実装は図形 1 つにつき GL.Begin/End を 1 組発行していたので、
                // 図形数がそのまま元のドローコール数にあたる。
                // 元の実装は図形 1 つにつき SetPass とドローコールを 1 組出していた。
                var ratio = GizmoBatch.LastPrimitiveCount / (float)GizmoBatch.LastDrawCallCount;
                EditorGUILayout.LabelField("　元比", $"約 1/{ratio:0.#}");
            }
        }

        private void DrawImmediateDrawDiagnostics()
        {
            EditorGUILayout.LabelField("即時描画の発行回数", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "シーンビューの IMGUI は、即時描画が挟まるたびに描画をやり直します。\n" +
                "回数が多いほど重くなるので、誰が出しているかをここで数えます。",
                EditorStyles.wordWrappedMiniLabel);

            bool enabled = EditorGUILayout.ToggleLeft("計測する", ImmediateDrawDiagnostics.Enabled);
            if (enabled != ImmediateDrawDiagnostics.Enabled) ImmediateDrawDiagnostics.SetEnabled(enabled);

            if (!enabled) return;

            double seconds = ImmediateDrawDiagnostics.ElapsedSeconds;
            EditorGUILayout.LabelField("　合計", $"{ImmediateDrawDiagnostics.Calls:N0} 回（{ImmediateDrawDiagnostics.Calls / seconds:N0} 回/秒）");
            EditorGUILayout.LabelField("　うちこのツールの担当内", $"{ImmediateDrawDiagnostics.CallsInsideOurScope:N0} 回");

            EditorGUILayout.LabelField("　呼び出し元（抜き取り）", EditorStyles.miniBoldLabel);
            foreach (var caller in ImmediateDrawDiagnostics.TopCallers)
            {
                EditorGUILayout.LabelField($"　　{caller.Key}", $"{caller.Value}");
            }

            if (GUILayout.Button("数え直す")) ImmediateDrawDiagnostics.Reset();
        }

        private void DrawDiagnostics()
        {
            _showDiagnostics = EditorGUILayout.Foldout(_showDiagnostics, "詳細");
            if (!_showDiagnostics) return;

            foreach (var line in VRCGizmoPatcher.PrimitiveStatus)
            {
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("当て直す")) VRCGizmoPatcher.Reinstall();
                if (GUILayout.Button("外す")) VRCGizmoPatcher.Uninstall();
            }
        }
    }
}
