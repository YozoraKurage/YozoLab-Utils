using System;
using YozoLab.SPS.Component;

namespace YozoLab.SPS.Model.StateAction {
    [Serializable]
    internal class SpsOnAction : Action {
        public SpsPlug target;
    }
}