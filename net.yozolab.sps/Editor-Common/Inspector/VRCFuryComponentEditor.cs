using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Component;
using YozoLab.SPS.Model;
using YozoLab.SPS.Model.Feature;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Inspector {
    [CustomEditor(typeof(SpsComponent), true)]
    internal class VRCFuryComponentEditor : VRCFuryComponentEditor<SpsComponent> {
        public static Func<UnityEngine.Component,string> getDebugLine;
        public static Action<VFGameObject, VisualElement> renderWarnings;

        internal static C CreateUpgradedClone<C>(C original, out GameObject cloneObject)
            where C : SpsComponent {
            cloneObject = new GameObject();
            cloneObject.SetActive(false);
            cloneObject.hideFlags |= HideFlags.HideAndDontSave;
            // Don't use AddComponent<C>, since C might not be the concrete type.
            var copy = (C)cloneObject.AddComponent(original.GetType());
            UnitySerializationUtils.CloneSerializable(original, copy);
            // Some migrations depend on the component's world transform. Don't use gameObjectOverride until after
            // upgrading, since an upgrade that adds components must not mutate the original prefab.
            cloneObject.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
            cloneObject.transform.localScale = original.transform.lossyScale;

            try {
                copy.Upgrade();
            } catch {
                DestroyImmediate(cloneObject);
                cloneObject = null;
                throw;
            }
            foreach (var component in cloneObject.GetComponents<SpsComponent>()) {
                component.gameObjectOverride = original.owner();
            }
            return copy;
        }

        protected override VisualElement CreateEditor(SerializedObject serializedObject, SpsComponent target) {
            return VRCFuryEditorUtils.Error("この種類のプロジェクトでは、この SPS コンポーネントは使えない");
        }
    }

    internal class VRCFuryComponentEditor<T> : UnityEditor.Editor where T : SpsComponent {
        private GameObject dummyObject;

        public sealed override VisualElement CreateInspectorGUI() {
            VisualElement versionLabel;
            if (VRCFuryComponentEditor.getDebugLine != null) {
                versionLabel = new Label(VRCFuryComponentEditor.getDebugLine.Invoke(target as UnityEngine.Component));
                versionLabel.AddToClassList("vfVersionLabel");
            } else {
                versionLabel = new VisualElement();
            }

            var content = new VisualElement();
            content.styleSheets.Add(VRCFuryEditorUtils.GetResource<StyleSheet>("SpsStyle.uss"));

            try {
                content.Add(CreateInspectorGUIUnsafe(versionLabel));
            } catch (Exception e) {
                Debug.LogException(new Exception("Failed to render editor", e));
                content.Add(versionLabel);
                content.Add(VRCFuryEditorUtils.Error("インスペクターを表示できなかった（詳しくは Console を見てください）"));
            }

            return content;
        }

        private VisualElement CreateInspectorGUIUnsafe(VisualElement versionLabel) {
            if (!(target is UnityEngine.Component c)) {
                return VRCFuryEditorUtils.Error("コンポーネントではない");
            }
            if (!(c is T v)) {
                return VRCFuryEditorUtils.Error("想定外の型");
            }

            var loadError = v.GetBrokenMessage();
            if (loadError != null) {
                return VRCFuryEditorUtils.Error(
                    $"この SPS コンポーネントを読み込めなかった（{loadError}）。YozoLab SPS を更新してください。");
            }
            
            var isInstance = PrefabUtility.IsPartOfPrefabInstance(v);

            var container = new VisualElement();

            if (isInstance) {
                // We prevent users from adding overrides on prefabs, because it does weird things (at least in unity 2019)
                // when you apply modifications to an object that lives within a SerializedReference. Some properties not overridden
                // will just be thrown out randomly, and unity will dump a bunch of errors.
                container.Add(CreatePrefabInstanceLabel(v));
            }

            container.Add(versionLabel);

            container.Add(CreateOverrideLabel());

            VisualElement body;
            if (isInstance) {
                OnDestroy();
                VRCFuryComponentEditor.CreateUpgradedClone(v, out dummyObject);
                var copyGameObject = dummyObject.asVf();
                // We need to prevent our added children from being bound to
                // the original component by unity
                body = new BindingBlock();
                body.SetEnabled(false);

                var children = copyGameObject.GetComponents<T>();
                if (children.Length != 1) body.Add(VRCFuryComponentHeader.CreateHeaderOverlay("古い形式（複数の設定）"));
                foreach (var child in children) {
                    var childSo = new SerializedObject(child);
                    var childEditor = _CreateEditor(childSo, child);
                    if (children.Length > 1) childEditor.AddToClassList("vrcfMultipleHeaders");
                    childEditor.Bind(childSo);
                    body.Add(childEditor); 
                }
            } else {
                v.Upgrade();
                if (v == null) return new VisualElement();
                serializedObject.Update();
                body = _CreateEditor(serializedObject, v);
            }
            
            container.Add(body);

            // アバター全体についての注意書きは、設定より前（一番上）に出す
            container.Insert(container.IndexOf(body), VRCFuryEditorUtils.Debug(refreshElement: () => {
                var warning = new VisualElement();

                if (c == null) return warning;

                if (VRCFuryComponentEditor.renderWarnings != null) {
                    VRCFuryComponentEditor.renderWarnings(c.owner(), warning);
                }

                var hasDelete = false;
                var isDeleted = EditorOnlyUtils.IsInsideEditorOnly(c.owner());
                if (isDeleted && !hasDelete) {
                    warning.Add(VRCFuryEditorUtils.Error(
                        "このコンポーネントは EditorOnly タグの付いたオブジェクトの中にあるので、ビルドでは何もしない。"));
                }
                
                return warning;
            }));

            return container;
        }

        public void OnDestroy() {
            if (dummyObject) {
                DestroyImmediate(dummyObject);
            }
        }
        
        private VisualElement _CreateEditor(SerializedObject serializedObject, T target) {
            var output = new VisualElement();
            var type = target.GetType();
            var attr = type.GetCustomAttribute<AddComponentMenu>();
            string title;
            if (attr != null) {
                title = attr.componentMenu;
                title = Regex.Replace(title, @".*/", "");
                title = Regex.Replace(title, @"\s*\((VRCFury|YozoLab SPS)\)$", "", RegexOptions.IgnoreCase);
            } else {
                title = target.GetType().Name;
                title = Regex.Replace(title, @"(a-z)([A-Z])", "$1 $2");
            }
            output.Add(VRCFuryComponentHeader.CreateHeaderOverlay(title));
            output.Add(CreateEditor(serializedObject, target));
            return output;
        }

        protected virtual VisualElement CreateEditor(SerializedObject serializedObject, T target) {
            return new VisualElement();
        }
        
        private VisualElement CreateOverrideLabel() {
            var baseText = "このインスタンスでプレハブの SPS の設定が上書きされている。元に戻してください" +
                           "（Apply すると、変えた設定のデータが壊れることがある）。";
            var overrideLabel = VRCFuryEditorUtils.Error(baseText);
            overrideLabel.SetVisible(false);

            void CheckOverride() {
                var vrcf = target as SpsComponent;
                if (vrcf == null) return;

                var mods = VRCFPrefabFixer.GetModifications(vrcf);
                var isModified = mods.Count > 0;
                overrideLabel.SetVisible(isModified);
                if (isModified) {
                    overrideLabel.Clear();
                    overrideLabel.Add(VRCFuryEditorUtils.WrappedLabel(baseText + "\n\n" + mods.Select(m => m.propertyPath).Join(", ")));
                }
            }

            //overrideLabel.schedule.Execute(CheckOverride).Every(1000);
            CheckOverride();

            return overrideLabel;
        }

        private VisualElement CreatePrefabInstanceLabel(UnityEngine.Component component) {
            void Open() {
                var componentInBasePrefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(component);
                var prefabPath = AssetDatabase.GetAssetPath(componentInBasePrefab);
                UnityCompatUtils.OpenPrefab(prefabPath, component.owner());
            }

            var content = new VisualElement();
            content.Add(VRCFuryEditorUtils.WrappedLabel(
                "プレハブのインスタンスなので、ここでは変更できない（プレハブの上書きで設定が壊れるのを防ぐため）。プレハブを開いて編集してください。"));
            var button = new Button(Open) { text = "プレハブを開いて編集" };
            button.style.alignSelf = Align.FlexStart;
            button.style.marginTop = 4;
            content.Add(button);
            return VRCFuryEditorUtils.Info(content);
        }
    }
}
