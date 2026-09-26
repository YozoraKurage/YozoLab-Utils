using System;
using UnityEngine;

namespace YozoLab.SPS.Model.StateAction {
    [Serializable]
    internal class ShaderInventoryAction : Action {
        public Renderer renderer;
        public int slot = 1;
    }
}