using System;

namespace YozoLab.SPS.Builder.Haptics {
    internal class SpsErrorMatException : Exception {
        public SpsErrorMatException() {
        }

        public SpsErrorMatException(string message) : base(message) {
        }
    }
}