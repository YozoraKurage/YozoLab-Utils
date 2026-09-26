using System;

namespace YozoLab.SPS.Model.StateAction {
    [Serializable]
    internal class FxFloatAction : Action {
        public string name;
        public float value = 1;
    }
}