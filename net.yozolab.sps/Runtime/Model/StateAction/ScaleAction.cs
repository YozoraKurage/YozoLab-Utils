using System;
using UnityEngine;

namespace YozoLab.SPS.Model.StateAction {
    [Serializable]
    internal class ScaleAction : Action {
        public GameObject obj;
        public float scale = 1;
    }
}