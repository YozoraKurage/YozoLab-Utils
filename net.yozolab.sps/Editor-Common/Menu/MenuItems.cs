namespace YozoLab.SPS.Menu {
    /**
     * メニューの置き場所。VRCFury の Tools/VRCFury/ と並ばないよう、YozoLab の下にまとめる。
     */
    internal static class MenuItems {
        private const string prefix = "Tools/YozoLab SPS/";

        public const string sps = prefix;
        public const int spsPriority = 1310;
        public const string createSocket = sps + "Create Socket";
        public const int createSocketPriority = spsPriority;
        public const string createPlug = sps + "Create Plug";
        public const int createPlugPriority = spsPriority + 1;
        public const string upgradeLegacyHaptics = sps + "Upgrade DPS to SPS";
        public const int upgradeLegacyHapticsPriority = spsPriority + 2;

        private const string settings = prefix + "Settings/";
        private const int settingsPriority = spsPriority + 100;
        public const string hapticToggle = settings + "Enable SPS Haptics";
        public const int hapticTogglePriority = settingsPriority + 1;
        public const string dpsAutoUpgrade = settings + "Auto-Upgrade DPS with contacts";
        public const int dpsAutoUpgradePriority = settingsPriority + 2;
        public const string spsDevMode = settings + "Enable SPS Internal Dev Mode";
        public const int spsDevModePriority = settingsPriority + 3;
    }
}
