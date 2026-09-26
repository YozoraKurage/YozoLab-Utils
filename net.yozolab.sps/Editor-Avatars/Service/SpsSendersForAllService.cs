using System.Linq;
using UnityEngine;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Menu;
using YozoLab.SPS.Utils;
using VRC.SDK3.Dynamics.Contact.Components;

namespace YozoLab.SPS.Service {
    [VFService]
    internal class SpsSendersForAllService {
        [VFAutowired] private readonly GlobalsService globals;
        
        [FeatureBuilderAction(FeatureOrder.GiveEverythingSpsSenders)]
        public void Apply() {
            // 古い DPS / TPS の構成に SPS の送受信を足し、他の人の SPS と反応し合えるようにする。
            // その過程でユーザーの TPS の送信側や古い目印を消すので、VRCFury と違い、設定
            // （Tools/YozoLab SPS/Settings/Auto-Upgrade DPS with contacts）を ON にしたときだけ行う。
            if (!AutoUpgradeDpsMenuItem.Get()) {
                return;
            }

            RemoveTPSSenders();
            SpsUpgrader.Apply(globals.avatarObject, false, SpsUpgrader.Mode.AutomatedForEveryone);
        }

        private void RemoveTPSSenders() {
            foreach (var sender in globals.avatarObject.GetComponentsInSelfAndChildren<VRCContactSender>()) {
                if (IsTPSSender(sender)) {
                    Debug.Log("Deleting TPS sender on " + sender.owner().GetDebugPath());
                    sender.Destroy();
                }
            }
        }

        private static bool IsTPSSender(VRCContactSender c) {
            if (c.collisionTags.Any(t => t == HapticUtils.CONTACT_PEN_MAIN)) return true;
            if (c.collisionTags.Any(t => t == HapticUtils.CONTACT_PEN_WIDTH)) return true;
            if (c.collisionTags.Any(t => t == HapticUtils.TagTpsOrfRoot)) return true;
            if (c.collisionTags.Any(t => t == HapticUtils.TagTpsOrfFront)) return true;
            return false;
        }
    }
}
