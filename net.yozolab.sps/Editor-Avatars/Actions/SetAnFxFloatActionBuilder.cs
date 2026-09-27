using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Feature;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("FX の Float パラメーターを設定")]
    internal class SetAnFxFloatActionBuilder : ActionBuilder<FxFloatAction> {
        public VFClip Build(FxFloatAction model) {
            var onClip = NewClip();
            if (string.IsNullOrWhiteSpace(model.name)) {
                return onClip;
            }

            if (VRChatGlobalParams.Names.Contains(model.name)) {
                throw new Exception("Set an FX Float cannot set built-in vrchat parameters");
            }

            onClip.SetAap(model.name, model.value);
            return onClip;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var col = new VisualElement();

            var row = new VisualElement().Row();
            row.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("name")).FlexGrow(1));
            row.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("value")).FlexBasis(30));
            col.Add(row);
                
            col.Add(VRCFuryEditorUtils.Warn(
                "このパラメーターはアニメーションで上書きされるようになるため、" +
                "メニューなど VRChat 側からは操作できなくなる。"));

            return col;
        }
    }
}
