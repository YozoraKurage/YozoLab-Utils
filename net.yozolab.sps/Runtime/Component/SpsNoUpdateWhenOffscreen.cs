using UnityEngine;

namespace YozoLab.SPS.Component {
    [AddComponentMenu("")]
    internal class SpsNoUpdateWhenOffscreen : SpsPlayComponent {
        private void Update() {
            var skin = GetComponent<SkinnedMeshRenderer>();
            if (skin == null) return;
            skin.updateWhenOffscreen = false;
        }
    }
}