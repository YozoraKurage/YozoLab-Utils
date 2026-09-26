using System;
using UnityEngine;

namespace YozoLab.SPS.Model.Feature {
    [Serializable]
    internal class TpsScaleFix : NewFeatureModel {
        [NonSerialized] public Renderer singleRenderer;
    }
}