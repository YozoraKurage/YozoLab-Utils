using YozoLab.SPS.Inspector;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("まばたきの無効化")]
    internal class BlockBlinkingActionBuilder : ActionBuilder<BlockBlinkingAction> {

        public VFClip Build(string actionName) {
            return NewClip();
        }

        [FeatureEditor]
        public static VisualElement Editor() {
            return VRCFuryEditorUtils.Warn("このアクションは YozoLab SPS では動作しません（VRCFury ではアバター全体のトラッキング制御を書き換えて実現していたため、持ち込んでいません）。");
        }
    }
}
