using System.Linq;
using YozoLab.SPS.Component;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Menu;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Service {
    /** Adds a parameter to the avatar so OGB can pick up what version of haptics are available */
    [VFService]
    internal class BakeHapticVersionsService {
        [VFAutowired] private readonly ControllersService controllers;
        [VFAutowired] private readonly VFGameObject avatarObject;
        private ControllerManager fx => controllers.GetFx();
        
        // Bump when plug senders or receivers are changed
        private const int LocalVersion = 10;

        [FeatureBuilderAction]
        public void Apply() {
            var hasOgbReceivers = avatarObject.GetComponentsInSelfAndChildren<SpsPlug>().Any(c => !c.fromSpsForAll)
                                  || avatarObject.GetComponentsInSelfAndChildren<SpsSocket>().Any(c => !c.fromSpsForAll);
            hasOgbReceivers &= HapticsToggleMenuItem.Get();

            if (hasOgbReceivers) {
                // Add a parameter to FX so it can be picked up by client apps
                // Because of https://feedback.vrchat.com/bug-reports/p/oscquery-provides-wrong-values-for-avatar-parameters-until-they-are-changed
                // we can't just use an int and set it to the version number.
                fx.NewBool($"VFH/Version/{LocalVersion}", usePrefix: false, synced: true, networkSynced: false);
            }
        }
    }
}
