using System.Collections.Generic;
using System.Linq;
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
    internal class SpsTouchReceiverBuilder {
        [VFAutowired] private readonly HapticContactsService hapticContacts;
        [VFAutowired] private readonly VFGameObject avatarObject;

        [FeatureBuilderAction]
        public void Apply() {
            var usedNames = new HashSet<string>();
            foreach (var receiver in avatarObject.GetComponentsInSelfAndChildren<SpsTouchReceiver>()) {
                var name = HapticUtils.MakeUniqueId(usedNames, HapticUtils.GetPreferredId(receiver, r => r.name, r => HapticUtils.GetFallbackId(r.owner())));
                var paramPrefix = "VFH/Zone/Touch/" + name.Replace('/','_');
                
                hapticContacts.AddReceiver(new HapticContactsService.ReceiverRequest() {
                    obj = receiver.owner(),
                    paramName = paramPrefix + "/Self",
                    objName = "Self",
                    radius = receiver.radius,
                    tags = HapticUtils.SelfContacts.Concat(new [] { HapticUtils.CONTACT_PEN_CLOSE }).ToArray(),
                    party = HapticUtils.ReceiverParty.Self,
                    worldScale = false,
                    usePrefix = false,
                    localOnly = true
                });
                hapticContacts.AddReceiver(new HapticContactsService.ReceiverRequest() {
                    obj = receiver.owner(),
                    paramName = paramPrefix + "/Others",
                    objName = "Others",
                    radius = receiver.radius,
                    tags = HapticUtils.BodyContacts.Concat(new [] { HapticUtils.CONTACT_PEN_CLOSE }).ToArray(),
                    party = HapticUtils.ReceiverParty.Others,
                    worldScale = false,
                    usePrefix = false,
                    localOnly = true
                });
            }
        }

        [CustomEditor(typeof(SpsTouchReceiver), true)]
        public class VRCFuryHapticTouchReceiverEditor : VRCFuryComponentEditor<SpsTouchReceiver> {
            protected override VisualElement CreateEditor(SerializedObject serializedObject, SpsTouchReceiver target) {
                var container = new VisualElement();
                
                container.Add(SpsPlugEditor.ConstraintWarning(target));

                var section = VRCFuryEditorUtils.Section("触れられる範囲",
                    "触れられると触覚デバイス（OGB）を動かす。球の中心に近いほど強くなる（中心で 100%）。" +
                    "手・指・足・SPS の Plug・Touch Sender、他の人の頭で反応する。Plug / Socket とは関係なく使える");
                section.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("radius"), "半径",
                    tooltip: "このオブジェクトのローカル単位"));
                section.Add(SpsEditorUtils.AutoHapticIdProp(
                    serializedObject.FindProperty("name"),
                    "OGB に送る名前",
                    target,
                    target.owner(),
                    avatar => avatar.GetComponentsInSelfAndChildren<SpsTouchReceiver>(),
                    receiver => HapticUtils.GetPreferredId(
                        receiver,
                        r => r.name,
                        r => HapticUtils.GetFallbackId(r.owner())
                    )
                ));
                container.Add(section);

                return container;
            }
        
            [DrawGizmo(GizmoType.Selected | GizmoType.Active | GizmoType.InSelectionHierarchy)]
            static void DrawGizmo(SpsTouchReceiver c, GizmoType gizmoType) {
                var worldPos = c.owner().worldPosition;
                var worldScale = c.owner().worldScale.x;
                VRCFuryGizmoUtils.DrawSphere(worldPos, worldScale * c.radius, Color.red);
                VRCFuryGizmoUtils.DrawText(worldPos, "SPS Touch Zone", Color.white, true);
            }
        }
    }
}
