using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("PhysBone のリセット")]
    [FeatureHideTitleInEditor]
    internal class ResetPhysboneActionBuilder : ActionBuilder<ResetPhysboneAction> {
        [VFAutowired] [CanBeNull] private readonly PhysboneResetService physboneResetService;

        public VFClip Build(ResetPhysboneAction model, string actionName) {
            var onClip = NewClip();
            if (model.physBone != null && physboneResetService != null) {
                var param = physboneResetService.CreatePhysBoneResetter(model.physBone.owner(), actionName);
                onClip.SetAap(param, 1);
            }
            return onClip;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var row = new VisualElement().Row();
            row.Add(VRCFuryActionDrawer.Title("PhysBone のリセット").FlexBasis(110));
            row.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("physBone")).FlexGrow(1));
            return row;
        }
    }
}
