using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Inspector {
    internal static class VRCFuryComponentHeader {
        private static VisualElement FindEditor(VisualElement el) {
            if (el == null) return null;
            if (el is InspectorElement) return el.parent;
            return FindEditor(el.parent);
        }

        private static VisualElement RenderHeader(string title, bool overlay) {
            if (!overlay) {
                var l = new Label(title).Bold();
                l.style.marginTop = 10;
                return l;
            }
            
            var headerArea = new VisualElement {
                style = {
                    height = 20,
                    width = Length.Percent(100),
                    top = -21,
                    position = Position.Absolute,
                },
                pickingMode = PickingMode.Ignore
            };

            Color backgroundColor = EditorGUIUtility.isProSkin
                ? new Color32(61, 61, 61, 255)
                : new Color32(194, 194, 194, 255);
            var row = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    height = 20,
                    backgroundColor = backgroundColor,
                    marginLeft = 18,
                    marginRight = 60,
                },
                pickingMode = PickingMode.Ignore
            };
            VRCFuryEditorUtils.HoverHighlight(row);
            headerArea.Add(row);

            // 「YozoLab SPS」の小さな札。元の見出しの文字の上に重ねて出す
            var label = new Label("SPS") {
                style = {
                    color = new Color(1f, 0.62f, 0.25f),
                    backgroundColor = new Color(1f, 0.55f, 0.1f, 0.16f),
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                    paddingLeft = 4,
                    paddingRight = 4,
                    marginTop = 2,
                    marginBottom = 2,
                    marginLeft = 2,
                    fontSize = 10,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    unityFontStyleAndWeight = FontStyle.Bold,
                },
                pickingMode = PickingMode.Ignore,
                tooltip = "YozoLab SPS"
            }.FlexShrink(0);
            row.Add(label);

            var name = new Label(title) {
                style = {
                    //color = Color.white,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    paddingLeft = 5
                },
                pickingMode = PickingMode.Ignore
            }.FlexGrow(1);
            row.Add(name);

            var wrapper = new VisualElement();
            wrapper.Add(headerArea);

            return wrapper;
        }

        private static bool HasMultipleHeaders(VisualElement root) {
            if (root == null) return false;
            if (root.ClassListContains("vrcfMultipleHeaders")) return true;
            return HasMultipleHeaders(root.parent);
        }

        private static void AttachHeaderOverlay(VisualElement body, string title) {
            var inspectorRoot = FindEditor(body);

            if (HasMultipleHeaders(body) || inspectorRoot == null) {
                body.Add(RenderHeader(title, false));
                return;
            }

            var headerIndex = inspectorRoot.Children()
                .Select((e, i) => (element: e, index: i))
                .Where(x => x.element.name.EndsWith("Header"))
                .Select(x => x.index)
                .DefaultIfEmpty(-1)
                .First();

            if (headerIndex < 0) {
                body.Add(RenderHeader(title, false));
                return;
            }

            var headerArea = RenderHeader(title, true);
            headerArea.AddToClassList("vrcfHeaderOverlay");
            inspectorRoot.Insert(headerIndex+1, headerArea);
            
            body.RegisterCallback<DetachFromPanelEvent>(e => {
                headerArea.parent?.Remove(headerArea);
            });
        }

        public static VisualElement CreateHeaderOverlay(string title) {
            var el = new VisualElement();
            el.AddToClassList("vrcfHeader");
            el.RegisterCallback<AttachToPanelEvent>(e => {
                AttachHeaderOverlay(el, title);
            });

            return el;
        }
    }
}
