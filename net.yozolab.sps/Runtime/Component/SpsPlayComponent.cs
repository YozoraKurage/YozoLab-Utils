using System;
using UnityEngine;
using YozoLab.SPS.VrcfEditorOnly;

namespace YozoLab.SPS.Component {
    internal abstract class SpsPlayComponent : MonoBehaviour, IVrcfEditorOnly {
        public static Action<SpsPlayComponent> onValidate;

        private void OnValidate() {
            onValidate?.Invoke(this);
        }
    }
}
