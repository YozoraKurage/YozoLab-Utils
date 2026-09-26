using System;
using System.Collections.Generic;
using UnityEngine;
using Action = YozoLab.SPS.Model.StateAction.Action;

namespace YozoLab.SPS.Model {
    [Serializable]
    internal class State {
        [SerializeReference] public List<Action> actions = new List<Action>();
    }
}