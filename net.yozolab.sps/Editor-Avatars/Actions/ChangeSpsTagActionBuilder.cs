using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("SPS タグの変更")]
    internal class ChangeSpsTagActionBuilder : ActionBuilder<ChangeSpsTagAction> {
        private enum TargetType {
            None,
            Plug,
            Socket
        }

        public VFClip Build(ChangeSpsTagAction model, bool debugMode) {
            var clip = NewClip();
            if (debugMode) return clip;
            if (TryGetBinding(model, out var target, out var type, out var lowPropertyName, out var highPropertyName, out var selfPropertyName, out var othersPropertyName)) {
                var tagHash = model.globalTag
                    ? (model.globalTagEnabled ? SpsConfigurer.SharedTag : 0u)
                    : SpsConfigurer.HashTag(SpsPlugEditor.SanitizeSpsTag(model.tag));
                clip.SetCurve(target, type, lowPropertyName, SpsMarkersService.GetLow(tagHash));
                clip.SetCurve(target, type, highPropertyName, SpsMarkersService.GetHigh(tagHash));
                if (selfPropertyName != null) {
                    clip.SetCurve(target, type, selfPropertyName, model.globalTag ? (model.globalTagEnabled ? 1 : 0) : (model.allowSelf ? 1 : 0));
                }
                if (othersPropertyName != null) {
                    clip.SetCurve(target, type, othersPropertyName, model.globalTag ? (model.globalTagEnabled ? 1 : 0) : (model.allowOthers ? 1 : 0));
                }
            }
            return clip;
        }

        private bool TryGetBinding(
            ChangeSpsTagAction model,
            out VFGameObject bindingTarget,
            out System.Type type,
            out string lowPropertyName,
            out string highPropertyName,
            out string selfPropertyName,
            out string othersPropertyName
        ) {
            bindingTarget = null;
            type = null;
            lowPropertyName = null;
            highPropertyName = null;
            selfPropertyName = null;
            othersPropertyName = null;

            var target = model.target;
            if (target == null) return false;

            var targetType = GetTargetType(target);
            var slot = GetClampedSlot(model, targetType);
            var targetObject = target.gameObject.asVf();

            if (targetType == TargetType.Plug) {
                bindingTarget = targetObject.Find("BakedSpsPlug/OneSpace/SpsResolver");
                if (bindingTarget == null) {
                    throw new Exception($"Change SPS Tag target `{targetObject.GetDebugPath()}` is missing `BakedSpsPlug/OneSpace/SpsResolver`");
                }
                type = typeof(MeshRenderer);
                if (model.globalTag) {
                    lowPropertyName = "material._SPS_TagInclude4Low";
                    highPropertyName = "material._SPS_TagInclude4High";
                    selfPropertyName = "material._SPS_TagInclude4Self";
                    othersPropertyName = "material._SPS_TagInclude4Others";
                } else {
                    var prefix = model.exclude ? "_SPS_TagExclude" : "_SPS_TagInclude";
                    lowPropertyName = $"material.{prefix}{slot}Low";
                    highPropertyName = $"material.{prefix}{slot}High";
                    selfPropertyName = $"material.{prefix}{slot}Self";
                    othersPropertyName = $"material.{prefix}{slot}Others";
                }
                return true;
            }

            if (targetType == TargetType.Socket) {
                bindingTarget = targetObject.Find("BakedSpsSocket/OneSpace/SpsScreenMarker");
                if (bindingTarget == null) {
                    throw new Exception($"Change SPS Tag target `{targetObject.GetDebugPath()}` is missing `BakedSpsSocket/OneSpace/SpsScreenMarker`");
                }
                type = typeof(MeshRenderer);
                lowPropertyName = $"material._SPS_SocketTag{slot}Low";
                highPropertyName = $"material._SPS_SocketTag{slot}High";
                return true;
            }

            return false;
        }

        private static int GetClampedSlot(ChangeSpsTagAction model, TargetType targetType) {
            var max = GetMaxSlot(targetType);
            if (max <= 0) return 1;
            return Mathf.Clamp(model.tagNumber, 1, max);
        }

        private static int GetMaxSlot(TargetType targetType) {
            switch (targetType) {
                case TargetType.Plug:
                    return SpsPlugEditor.SpsTagRuleCount;
                case TargetType.Socket:
                    return SpsSocketEditor.SpsTagCount;
                default:
                    return Mathf.Max(SpsPlugEditor.SpsTagRuleCount, SpsSocketEditor.SpsTagCount);
            }
        }

        private static TargetType GetTargetType(Transform target) {
            if (target == null) return TargetType.None;
            if (target.GetComponent<SpsPlug>() != null) return TargetType.Plug;
            if (target.GetComponent<SpsSocket>() != null) return TargetType.Socket;
            return TargetType.None;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var targetProp = prop.FindPropertyRelative("target");
            var excludeProp = prop.FindPropertyRelative("exclude");
            var globalTagProp = prop.FindPropertyRelative("globalTag");
            var globalTagEnabledProp = prop.FindPropertyRelative("globalTagEnabled");
            var allowSelfProp = prop.FindPropertyRelative("allowSelf");
            var allowOthersProp = prop.FindPropertyRelative("allowOthers");
            var tagNumberProp = prop.FindPropertyRelative("tagNumber");
            var tagProp = prop.FindPropertyRelative("tag");

            return VRCFuryEditorUtils.RefreshOnChange(() => {
                var content = new VisualElement();
                content.Add(VRCFuryEditorUtils.Prop(targetProp, "対象", tooltip: "SPS Plug または SPS Socket が付いたオブジェクト"));

                var targetType = GetTargetType(targetProp.objectReferenceValue as Transform);
                var maxSlot = GetMaxSlot(targetType);
                var clampedSlot = Mathf.Clamp(tagNumberProp.intValue <= 0 ? 1 : tagNumberProp.intValue, 1, maxSlot);
                if (tagNumberProp.intValue != clampedSlot) {
                    tagNumberProp.intValue = clampedSlot;
                    tagNumberProp.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                }

                if (targetType == TargetType.Plug) {
                    content.Add(new Label("変更するタグ:"));
                    var includeButton = new Toggle {
                        value = !excludeProp.boolValue && !globalTagProp.boolValue
                    };
                    var excludeButton = new Toggle {
                        value = excludeProp.boolValue && !globalTagProp.boolValue
                    };
                    var globalButton = new Toggle {
                        value = globalTagProp.boolValue
                    };
                    includeButton.RegisterValueChangedCallback(cb => {
                        if (!cb.newValue) return;
                        globalTagProp.boolValue = false;
                        excludeProp.boolValue = false;
                        excludeProp.serializedObject.ApplyModifiedProperties();
                        excludeButton.SetValueWithoutNotify(false);
                        globalButton.SetValueWithoutNotify(false);
                    });
                    excludeButton.RegisterValueChangedCallback(cb => {
                        if (!cb.newValue) return;
                        globalTagProp.boolValue = false;
                        excludeProp.boolValue = true;
                        excludeProp.serializedObject.ApplyModifiedProperties();
                        includeButton.SetValueWithoutNotify(false);
                        globalButton.SetValueWithoutNotify(false);
                    });
                    globalButton.RegisterValueChangedCallback(cb => {
                        if (!cb.newValue) return;
                        globalTagProp.boolValue = true;
                        excludeProp.serializedObject.ApplyModifiedProperties();
                        includeButton.SetValueWithoutNotify(false);
                        excludeButton.SetValueWithoutNotify(false);
                    });
                    var row = new VisualElement().Row();
                    row.style.flexWrap = Wrap.NoWrap;
                    var includeProp = VRCFuryEditorUtils.Prop(null, "含める", fieldOverride: includeButton);
                    includeProp.style.marginRight = 12;
                    row.Add(includeProp);
                    var excludeUi = VRCFuryEditorUtils.Prop(null, "除外", fieldOverride: excludeButton);
                    excludeUi.style.marginRight = 12;
                    row.Add(excludeUi);
                    row.Add(VRCFuryEditorUtils.Prop(null, "グローバル", fieldOverride: globalButton));
                    content.Add(row);
                }

                if (targetType == TargetType.Plug && globalTagProp.boolValue) {
                    content.Add(new Label("設定する値（空欄で解除）:"));
                    content.Add(VRCFuryEditorUtils.Prop(globalTagEnabledProp, "有効"));
                } else {
                    if (targetType == TargetType.Socket) {
                        content.Add(new Label("変更するタグ:"));
                    }
                    tagNumberProp.intValue = clampedSlot;
                    content.Add(VRCFuryEditorUtils.Prop(tagNumberProp, "タグ番号", onChange: () => {
                        tagNumberProp.intValue = Mathf.Clamp(tagNumberProp.intValue <= 0 ? 1 : tagNumberProp.intValue, 1, maxSlot);
                        tagNumberProp.serializedObject.ApplyModifiedProperties();
                    }));
                    content.Add(new Label("設定する値（空欄で解除）:"));
                    content.Add(SpsPlugEditor.SpsTagProp(tagProp, "タグ"));
                }

                if (targetType == TargetType.Plug && !globalTagProp.boolValue) {
                    var row = new VisualElement().Row();
                    row.style.flexWrap = Wrap.NoWrap;
                    var selfProp = VRCFuryEditorUtils.Prop(allowSelfProp, "自分");
                    selfProp.style.marginRight = 12;
                    row.Add(selfProp);
                    row.Add(VRCFuryEditorUtils.Prop(allowOthersProp, "他の人"));
                    content.Add(row);
                }

                if (targetType == TargetType.None && targetProp.objectReferenceValue != null) {
                    content.Add(VRCFuryEditorUtils.Warn("対象には SPS Plug か SPS Socket が付いたオブジェクトを指定する。"));
                }

                return content;
            }, targetProp, excludeProp, globalTagProp, globalTagEnabledProp, allowSelfProp, allowOthersProp, tagNumberProp);
        }
    }
}
