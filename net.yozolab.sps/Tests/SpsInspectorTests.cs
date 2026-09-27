using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Component;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Tests {
    /**
     * 各 SPS コンポーネントのインスペクターが、例外を出さずに組み上がるか。
     * （見た目そのものは確かめられないので、中身の文字を書き出しておく。）
     */
    [Category("YozoLab SPS")]
    public class SpsInspectorTests {
        private readonly List<Object> cleanup = new List<Object>();

        [TearDown]
        public void TearDown() {
            foreach (var o in cleanup) if (o != null) Object.DestroyImmediate(o);
            cleanup.Clear();
        }

        private static IEnumerable<System.Type> ComponentTypes() {
            yield return typeof(SpsPlug);
            yield return typeof(SpsSocket);
            yield return typeof(SpsTouchReceiver);
            yield return typeof(SpsTouchSender);
            yield return typeof(SpsOptionsComponent);
        }

        [TestCaseSource(nameof(ComponentTypes))]
        public void Inspector_Builds(System.Type type) {
            var go = new GameObject(type.Name);
            cleanup.Add(go);
            var component = go.AddComponent(type);
            if (component is SpsSocket socket) {
                // 中継点と深度アニメーションのある状態も組ませる
                socket.guidedPathStops.Add(new SpsSocket.GuidedPathStop { transform = go.transform });
                socket.depthActions2.Add(new SpsSocket.DepthActionNew());
            }
            if (component is SpsPlug plug) {
                plug.depthActions2.Add(new SpsSocket.DepthActionNew());
                plug.includeTags.Add(new SpsPlug.TagRule { tag = "test" });
            }

            var editor = Editor.CreateEditor(component);
            cleanup.Add(editor);
            var root = editor.CreateInspectorGUI();
            Assert.IsNotNull(root);

            var texts = new List<string>();
            Collect(root, texts, 0);
            var dump = string.Join("\n", texts);
            Debug.Log($"[SPS INSPECTOR] {type.Name}\n{dump}");
            StringAssert.DoesNotContain("インスペクターを表示できなかった", dump);
            StringAssert.DoesNotContain("表示の更新に失敗", dump);
        }

        private static void Collect(VisualElement e, List<string> output, int depth) {
            if (e.style.display == DisplayStyle.None) return;
            string text = null;
            switch (e) {
                case Foldout f: text = "▼ " + f.text; break;
                case Button b: text = "[" + b.text + "]"; break;
                case Toggle t when !string.IsNullOrEmpty(t.label): text = "☐ " + t.label; break;
                case TextElement t when !(e.parent is Button) && !string.IsNullOrWhiteSpace(t.text): text = t.text; break;
            }
            if (!string.IsNullOrEmpty(e.tooltip)) text = (text ?? "") + $"  (tip: {e.tooltip})";
            if (text != null) output.Add(new string(' ', depth) + text.Replace("\n", " / "));
            foreach (var c in e.Children()) Collect(c, output, text != null ? depth + 1 : depth);
        }
    }
}
