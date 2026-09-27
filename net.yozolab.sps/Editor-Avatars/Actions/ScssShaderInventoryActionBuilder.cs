using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("SCSS シェーダーインベントリ")]
    internal class ScssShaderInventoryActionBuilder : ActionBuilder<ShaderInventoryAction> {
        public VFClip Build(ShaderInventoryAction model) {
            return MakeClip(model, 1);
        }
        public VFClip BuildOff(ShaderInventoryAction model) {
            return MakeClip(model, 0);
        }

        private VFClip MakeClip(ShaderInventoryAction model, float value) {
            var clip = NewClip();
            var renderer = model.renderer;
            if (renderer == null) return clip;
            var propertyName = $"material._InventoryItem{model.slot:D2}Animated";
            clip.SetCurve(renderer, propertyName, value);
            return clip;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var output = new VisualElement();
            output.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("renderer"), "レンダラー"));
            output.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("slot"), "スロット番号"));
            return output;
        }
    }
}
