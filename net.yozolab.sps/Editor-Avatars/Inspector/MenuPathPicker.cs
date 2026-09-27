using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YozoLab.SPS.Inspector {
    /**
     * アバターのメニューからパスを選ばせるボタン。
     * VRCFury では VRCFury の各機能を反映した後のメニューの見込みから選ばせていたが、
     * ここではアバターに設定されている実際のメニューから選ばせる。
     */
    internal static class MenuPathPicker {
        public static VisualElement SelectButton(
            [CanBeNull] VFGameObject avatarObject,
            bool foldersOnly,
            SerializedProperty prop,
            string label = "メニューのパス",
            Func<string> append = null,
            string selectLabel = "選択",
            string tooltip = null,
            bool immediate = false,
            Vector2? pos = null
        ) {
            void Apply(string path) {
                if (append != null) {
                    if (path != "") path += "/";
                    path += append();
                }
                prop.stringValue = path;
                prop.serializedObject.ApplyModifiedProperties();
            }
            
            void OnClick() {
                if (avatarObject == null) return;

                var controlPaths = new List<IList<string>>();
                var rootMenu = avatarObject.GetComponent<VRCAvatarDescriptor>()?.expressionsMenu;
                if (rootMenu == null) return;
                rootMenu.ForEachMenu(ForEachItem: (control, path) => {
                    if (!foldersOnly || control.type == VRCExpressionsMenu.Control.ControlType.SubMenu) {
                        controlPaths.Add(path);
                    }

                    return VRCExpressionsMenuExtensions.ForEachMenuItemResult.Continue;
                });

                string PathToString(IList<string> path) {
                    return path.Select(p => p.Replace("/", "\\/")).Join('/');
                }

                void AddItem(VrcfSearchWindow.Group group, IList<string> prefix) {
                    var children = controlPaths
                        .Where(path => path.Count == prefix.Count + 1)
                        .Where(path => prefix.Select((segment, i) => path[i] == segment).All(c => c))
                        .ToList();
                    if (prefix.Count == 0) {
                        if (foldersOnly) {
                            group.Add("<このフォルダを選択>", "");
                        }

                        foreach (var child in children) {
                            AddItem(group, child);
                        }
                    } else {
                        if (children.Count > 0) {
                            var subGroup = group.AddGroup(prefix.Last());
                            subGroup.Add("<このフォルダを選択>", PathToString(prefix));
                            foreach (var child in children) {
                                AddItem(subGroup, child);
                            }
                        } else {
                            group.Add(prefix.Last(), PathToString(prefix));
                        }
                    }
                }

                var window = new VrcfSearchWindow("アバターのメニュー");
                AddItem(window.GetMainGroup(), new string[] { });

                window.Open(Apply, pos);
            }

            if (immediate) {
                OnClick();
                return null;
            }

            var row = new VisualElement().Row();
            row.Add(VRCFuryEditorUtils.Prop(prop, label, tooltip: tooltip).FlexGrow(1));
            row.Add(new Button(OnClick) { text = selectLabel });
            return row;
        }
    }
}
