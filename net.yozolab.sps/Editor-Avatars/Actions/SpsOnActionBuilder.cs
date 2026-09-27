using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("SPS の有効化")]
    [FeatureHideTitleInEditor]
    internal class SpsOnActionBuilder : ActionBuilder<SpsOnAction> {
        public VFClip Build(SpsOnAction model) {
            return MakeClip(model, true);
        }
        public VFClip BuildOff(SpsOnAction model) {
            return MakeClip(model, false);
        }

        private VFClip MakeClip(SpsOnAction model, bool enabled) {
            var clip = NewClip();
            if (model.target == null) {
                //Debug.LogWarning("Missing target in action: " + name);
                return clip;
            }
            clip.SetCurve(model.target, "spsAnimatedEnabled", enabled ? 1 : 0);
            return clip;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var row = new VisualElement().Row();
            row.Add(VRCFuryActionDrawer.Title("SPS の有効化").FlexBasis(110));
            row.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("target")).FlexGrow(1));
            return row;
        }
    }
}
