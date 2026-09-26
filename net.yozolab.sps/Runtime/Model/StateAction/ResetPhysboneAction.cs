using System;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace YozoLab.SPS.Model.StateAction {
    [Serializable]
    internal class ResetPhysboneAction : Action {
        public VRCPhysBone physBone;
    }
}