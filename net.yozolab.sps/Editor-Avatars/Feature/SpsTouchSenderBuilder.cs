using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Feature {
    [VFService]
    internal class SpsTouchSenderBuilder {
        [VFAutowired] private readonly HapticContactsService hapticContacts;
        [VFAutowired] private readonly VFGameObject avatarObject;
        [VFAutowired] private readonly ClosestBoneUtils closestBoneUtils;

        [FeatureBuilderAction]
        public void Apply() {
            foreach (var sender in avatarObject.GetComponentsInSelfAndChildren<SpsTouchSender>()) {
                HapticSenderFactory.AddSender(new HapticSenderFactory.SenderRequest() {
                    obj = sender.owner(),
                    objName = "Sender",
                    radius = sender.radius,
                    tags = new string[] { "Finger", "FingerR", "FingerIndex", "FingerIndexR" },
                    worldScale = false,
                    isOnHips = closestBoneUtils.GetClosestHumanoidBone(sender.owner()) == HumanBodyBones.Hips
                });
            }
        }
        
        [CustomEditor(typeof(SpsTouchSender), true)]
        public class VRCFuryHapticTouchSenderEditor : VRCFuryComponentEditor<SpsTouchSender> {
            protected override VisualElement CreateEditor(SerializedObject serializedObject, SpsTouchSender target) {
                var container = new VisualElement();
                
                container.Add(SpsPlugEditor.ConstraintWarning(target));

                var section = VRCFuryEditorUtils.Section("触れる側",
                    "SPS の Touch Zone や Socket の触覚を反応させる当たり判定を足す（「Finger」タグの Contact Sender と同じ）");
                section.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("radius"), "半径",
                    tooltip: "このオブジェクトのローカル単位"));
                container.Add(section);

                return container;
            }
        
            [DrawGizmo(GizmoType.Selected | GizmoType.Active | GizmoType.InSelectionHierarchy)]
            static void DrawGizmo(SpsTouchSender c, GizmoType gizmoType) {
                var worldPos = c.owner().worldPosition;
                var worldScale = c.owner().worldScale.x;
                VRCFuryGizmoUtils.DrawSphere(worldPos, worldScale * c.radius, Color.blue);
                VRCFuryGizmoUtils.DrawText(worldPos, "Touch Sender", Color.white, true);
            }
        }
    }
}
