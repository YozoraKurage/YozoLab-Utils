using System.Linq;
using System.Text.RegularExpressions;
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
    [FeatureTitle("フリップブック")]
    internal class FlipBookBuilderActionBuilder : ActionBuilder<FlipBookBuilderAction> {
        [VFAutowired] [CanBeNull] private readonly ClipBuilderService clipBuilder;

        public VFClip Build(FlipBookBuilderAction model, VFGameObject animObject, ActionClipService actionClipService) {
            var onClip = NewClip();
            var states = model.pages.Select(page => page.state).ToList();
            if (states.Count == 0) return onClip;
            // Duplicate the last state so the last state still gets an entire frame
            states.Add(states.Last());
            var sources = states
                .Select((substate,i) => {
                    var loaded = actionClipService.LoadStateAdv("tmp", substate, animObject);
                    return ((float)i, loaded.onClip.EvaluateMotion(1).FlattenToClip(VFMotionFlattenMode.DefaultVisibleClips));
                })
                .ToArray();

            if (clipBuilder != null) {
                var built = clipBuilder.MergeSingleFrameClips(sources);
                built.UseConstantTangents();
                onClip.CopyFrom(built);
            } else {
                // This is wrong, but it's fine because this branch is for debug info only
                foreach (var (time,source) in sources) {
                    onClip.CopyFrom(source);
                }
            }
            return onClip;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var output = new VisualElement();
            output.Add(VRCFuryEditorUtils.Info(
                "各ページのアクションを 1 フレームずつ並べたクリップを作る。" +
                "ラジアル（スライダー）で操作するトグルと組み合わせ、各ページにプリセットを入れておくと" +
                "スライダーでどれか 1 つを選べる。"
            ));
            output.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("pages")));
            return output;
        }

        [CustomPropertyDrawer(typeof(FlipBookBuilderAction.FlipBookPage))]
        public class FlipbookPageDrawer : PropertyDrawer {
            public override VisualElement CreatePropertyGUI(SerializedProperty prop) {
                var content = new VisualElement();
                var match = Regex.Match(prop.propertyPath, @"\[(\d+)\]$");
                string pageNum;
                if (match.Success && int.TryParse(match.Groups[1].ToString(), out var num)) {
                    pageNum = (num + 1).ToString();
                } else {
                    pageNum = "?";
                }
                content.Add(new Label($"ページ {pageNum}").Bold());
                content.Add(VRCFuryActionSetDrawer.render(prop.FindPropertyRelative("state"), showDebugInfo: false));
                return content;
            }
        }
    }
}
