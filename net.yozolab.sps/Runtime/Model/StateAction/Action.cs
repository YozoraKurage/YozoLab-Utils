using System;
using UnityEngine.Serialization;
using YozoLab.SPS.Upgradeable;

namespace YozoLab.SPS.Model.StateAction {
    [Serializable]
    internal class Action : VrcfUpgradeable {
        public bool desktopActive = false;
        public bool androidActive = false;

        public bool localOnly = false;
        public bool remoteOnly = false;
    }
}
