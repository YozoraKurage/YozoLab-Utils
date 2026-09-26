using YozoLab.SPS.Injector;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Service {
    /**
     * Holds one-shot initialization behaviours in a single Action layer.
     */
    [VFService]
    internal class InitBehavioursService {
        [VFAutowired] private readonly ControllersService controllers;

        private VFState state;

        public VFState GetState() {
            if (state != null) return state;

            var actionController = controllers.GetAction();
            var layer = actionController.NewLayer("VRCF Init Behaviours");
            layer.weight = 0;
            return state = layer.NewState("Init");
        }
    }
}
