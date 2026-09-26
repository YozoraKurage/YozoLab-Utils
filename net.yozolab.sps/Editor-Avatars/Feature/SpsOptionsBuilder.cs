using YozoLab.SPS.Component;
using UnityEditor;
using UnityEngine.UIElements;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.Feature;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Feature {
    [FeatureTitle("SPS Options")]
    [FeatureOnlyOneAllowed]
    [FeatureRootOnly]
    internal class SpsOptionsBuilder : FeatureBuilder<SpsOptions> {

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop, VFGameObject avatarObject) {
            var c = new VisualElement();
            c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("menuIcon"), "SPS Menu Icon Override"));
            
            var pathProp = prop.FindPropertyRelative("menuPath");
            c.Add(MenuPathPicker.SelectButton(
                avatarObject,
                true,
                pathProp,
                append: () => "SPS",
                label: "SPS Menu Path Override (Default: SPS)",
                selectLabel: "Select"
            ));

            c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("saveSockets"), "Save Sockets Between Worlds"));
            c.Add(VRCFuryEditorUtils.Prop(
                prop.FindPropertyRelative("legacyModeEnabledOnAvatarLoad"),
                "Legacy Mode enabled on avatar load"
            ));
            return c;
        }
    }

    [CustomEditor(typeof(SpsOptionsComponent), true)]
    internal class SpsOptionsComponentEditor : VRCFuryComponentEditor<SpsOptionsComponent> {
        protected override VisualElement CreateEditor(SerializedObject serializedObject, SpsOptionsComponent target) {
            return SpsOptionsBuilder.Editor(serializedObject.FindProperty("options"), target.owner().GetAvatarRoot());
        }
    }
}
