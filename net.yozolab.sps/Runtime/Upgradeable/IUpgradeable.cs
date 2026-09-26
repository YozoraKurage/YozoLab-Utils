using UnityEngine;

namespace YozoLab.SPS.Upgradeable {
    internal interface IUpgradeable : ISerializationCallbackReceiver {
        bool Upgrade(int fromVersion);
        int GetLatestVersion();
        int Version { get; set; }
    }
}
