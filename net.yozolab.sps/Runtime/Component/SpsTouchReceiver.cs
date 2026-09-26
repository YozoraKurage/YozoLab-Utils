using UnityEngine;

namespace YozoLab.SPS.Component {
    [AddComponentMenu("YozoLab/SPS/SPS Touch Zone")]
    internal class SpsTouchReceiver : SpsComponent {
        public new string name;
        public float radius = 0.1f;
    }
}
