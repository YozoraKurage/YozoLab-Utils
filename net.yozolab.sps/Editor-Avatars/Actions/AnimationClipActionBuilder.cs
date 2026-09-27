using System.Collections.Immutable;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("アニメーションクリップ")]
    [FeatureHideTitleInEditor]
    internal class AnimationClipActionBuilder : ActionBuilder<AnimationClipAction> {
        [VFAutowired] private readonly VFGameObject avatarObject;
        [VFAutowired] private readonly VRCFObjectPathCache objectPaths;

        public VFMotion Build(AnimationClipAction clipAction, VFGameObject animObject) {
            VFMotion copy;

            if (clipAction.vfClip is VFMotion vfClip) {
                copy = vfClip.Clone();
            } else {
                var input = clipAction.motion;
                if (input == null) input = clipAction.clip.Get();
                if (input == null) return NewClip();

                copy = VFMotion.Load(
                    input,
                    new VFLoadContext {
                        OwnerObject = animObject,
                        AnimatorObject = avatarObject,
                        AdjustRootScale = true,
                        ObjectPaths = objectPaths
                    }
                );
            }

            // 人型のマッスルは FX では扱えない。VRCFury は全身エモート用のレイヤーへ回していたが、
            // それはアバター全体（Action レイヤー）への書き込みになるので持ち込まず、消すだけにする。
            foreach (var clip in new AnimatorIterator.Clips().From(copy)) {
                clip.Rewrite(AnimationRewriter.RewriteBinding(b => {
                    if (b.GetPropType() == EditorCurveBindingType.Muscle) return null;
                    return b;
                }));
            }

            return copy;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop, VFGameObject componentObject) {
            var row = new VisualElement().Row();
            row.Add(VRCFuryActionDrawer.Title("アニメーションクリップ").FlexBasis(110));
            var clipProp = prop.FindPropertyRelative("clip");
            row.Add(VRCFuryEditorUtils.Prop(clipProp).FlexGrow(1));
            row.Add(new Button(() => {
                var clip = (clipProp.GetObject() as GuidAnimationClip)?.Get();
                if (clip == null) {
                    var newPath = EditorUtility.SaveFilePanelInProject("YozoLab SPS レコーダー", "New Animation", "anim", "新しいアニメーションの保存先");
                    if (string.IsNullOrEmpty(newPath)) return;
                    clip = VFClip.Create().Save(componentObject) as AnimationClip;
                    VRCFuryAssetDatabase.SaveAsset(clip, newPath);
                    GuidWrapperPropertyDrawer.SetValue(clipProp, clip);
                    clipProp.serializedObject.ApplyModifiedProperties();
                }
                RecorderUtils.Record(clip, componentObject);
            }) { text = "記録", tooltip = "アニメーションの記録を始める。クリップが未設定なら新規作成する" });
            return row;
        }
        
    }
}
