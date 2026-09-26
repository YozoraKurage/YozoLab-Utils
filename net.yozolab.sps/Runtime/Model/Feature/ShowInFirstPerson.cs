using System;
using UnityEngine;

namespace YozoLab.SPS.Model.Feature {
    [Serializable]
    internal class ShowInFirstPerson : NewFeatureModel {
        [NonSerialized] public bool useObjOverride = false;
        [NonSerialized] public GameObject objOverride = null;
        [NonSerialized] public bool onlyIfChildOfHead = false;
    }
}