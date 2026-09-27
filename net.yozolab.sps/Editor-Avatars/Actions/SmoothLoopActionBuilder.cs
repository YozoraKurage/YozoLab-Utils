using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("なめらかなループ（呼吸など）")]
    internal class SmoothLoopActionBuilder : ActionBuilder<SmoothLoopAction> {
        [VFAutowired] [CanBeNull] private readonly ClipBuilderService clipBuilder;

        public VFClip Build(SmoothLoopAction model, ActionClipService actionClipService, VFGameObject animObject) {
            var onClip = NewClip();
            var clip1 = actionClipService.LoadStateAdv("tmp", model.state1, animObject);
            var clip2 = actionClipService.LoadStateAdv("tmp", model.state2, animObject);

            if (clipBuilder != null) {
                var built = clipBuilder.MergeSingleFrameClips(
                    (0, clip1.onClip.EvaluateMotion(1).FlattenToClip(VFMotionFlattenMode.DefaultVisibleClips)),
                    (model.loopTime / 2, clip2.onClip.EvaluateMotion(1).FlattenToClip(VFMotionFlattenMode.DefaultVisibleClips)),
                    (model.loopTime, clip1.onClip.EvaluateMotion(1).FlattenToClip(VFMotionFlattenMode.DefaultVisibleClips))
                );
                onClip.CopyFrom(built);
            } else {
                // This is wrong, but it's fine because this branch is for debug info only
                onClip.CopyFrom(clip1.onClip.EvaluateMotion(1).FlattenToClip(VFMotionFlattenMode.DefaultVisibleClips));
                onClip.CopyFrom(clip2.onClip.EvaluateMotion(1).FlattenToClip(VFMotionFlattenMode.DefaultVisibleClips));
            }

            onClip.SetLooping(true);
            return onClip;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var output = new VisualElement();
            output.Add(VRCFuryEditorUtils.Info(
                "2 つの状態の間をなめらかに行き来するループアニメーションを作る。" +
                "呼吸など、2 状態を繰り返す動きに使える。"));
            output.Add(VRCFuryActionSetDrawer.render(prop.FindPropertyRelative("state1"), "状態 A", showDebugInfo: false));
            output.Add(VRCFuryActionSetDrawer.render(prop.FindPropertyRelative("state2"), "状態 B", showDebugInfo: false));
            output.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("loopTime"), "周期（秒）"));
            return output;
        }
    }
}
