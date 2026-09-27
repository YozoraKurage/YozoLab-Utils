using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("Poiyomi フリップブックのフレーム")]
    internal class PoiyomiFlipbookFrameActionBuilder : ActionBuilder<FlipbookAction> {
        public VFClip Build(FlipbookAction model) {
            var clip = NewClip();
            var renderer = model.renderer;
            if (renderer == null) return clip;

            // If we animate the frame to a flat number, unity can internally do some weird tweening
            // which can result in it being just UNDER our target, (say 0.999 instead of 1), resulting
            // in unity displaying frame 0 instead of 1. Instead, we target framenum+0.5, so there's
            // leniency around it.
            var frameAnimNum = (float)(Math.Floor((double)model.frame) + 0.5);
            clip.SetCurve(renderer, "material._FlipbookCurrentFrame", frameAnimNum);
            return clip;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var output = new VisualElement();
            output.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("renderer"), "レンダラー"));
            output.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("frame"), "フレーム番号"));
            return output;
        }
    }
}
