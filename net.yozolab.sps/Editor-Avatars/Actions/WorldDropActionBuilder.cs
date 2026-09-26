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
    [FeatureTitle("World Drop")]
    [FeatureHideTitleInEditor]
    internal class WorldDropActionBuilder : ActionBuilder<WorldDropAction> {
        
        public VFClip Build(WorldDropAction model, string actionName) {
            return NewClip();
        }
        
        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var unsupported = VRCFuryEditorUtils.Warn("このアクションは YozoLab SPS では動作しません（VRCFury ではアバター全体のオブジェクト配置を書き換えて実現していたため、持ち込んでいません）。");
            var row = new VisualElement().Row();
            row.Add(VRCFuryActionDrawer.Title("World Drop").FlexBasis(100));
            row.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("obj")).FlexGrow(1));
            var col = new VisualElement();
            col.Add(row);
            col.Add(unsupported);
            return col;
        }
    }
}
