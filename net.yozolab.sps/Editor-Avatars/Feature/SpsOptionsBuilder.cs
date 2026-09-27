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
            var c = VRCFuryEditorUtils.Section("SPS のメニュー", "アバターに 1 つだけ置く");
            c.Add(VRCFuryEditorUtils.BetterProp(prop.FindPropertyRelative("menuIcon"), "アイコン",
                tooltip: "空なら既定のアイコン"));
            
            var pathProp = prop.FindPropertyRelative("menuPath");
            c.Add(MenuPathPicker.SelectButton(
                avatarObject,
                true,
                pathProp,
                append: () => "SPS",
                label: "置き場所（空なら「SPS」）",
                selectLabel: "選択"
            ));

            c.Add(VRCFuryEditorUtils.BetterProp(prop.FindPropertyRelative("saveSockets"), "Socket のオン/オフをワールド間で保存する"));
            c.Add(VRCFuryEditorUtils.BetterProp(
                prop.FindPropertyRelative("legacyModeEnabledOnAvatarLoad"),
                "アバターを読み込んだとき古い形式のモードを ON にする",
                tooltip: "古い形式（SPS1 / DPS / TPS）の Plug と組み合わせるためのモード"
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
