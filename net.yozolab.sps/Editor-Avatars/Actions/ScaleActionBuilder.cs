using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("スケール")]
    internal class ScaleActionBuilder : ActionBuilder<ScaleAction> {
        public VFClip Build(ScaleAction model) {
            var clip = NewClip();
            if (model.obj == null) return clip;
            var localScale = model.obj.asVf().localScale;
            var newScale = localScale * model.scale;
            clip.SetScale(model.obj, newScale);
            return clip;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var row = new VisualElement();
            row.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("obj"), "オブジェクト"));
            row.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("scale"), "倍率"));
            return row;
        }
    }
}
