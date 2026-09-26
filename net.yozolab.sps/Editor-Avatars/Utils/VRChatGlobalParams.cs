using System.Collections.Generic;

namespace YozoLab.SPS.Utils {
    /**
     * VRChat が自前で値を入れる組み込みパラメータ。
     * （VRCFury では FullControllerBuilder が持っていた一覧。SPS 側からはこれだけを使う。）
     */
    internal static class VRChatGlobalParams {
        public static readonly HashSet<string> Names = new HashSet<string> {
            "IsLocal",
            "PreviewMode",
            "Viseme",
            "Voice",
            "GestureLeft",
            "GestureRight",
            "GestureLeftWeight",
            "GestureRightWeight",
            "AngularY",
            "VelocityX",
            "VelocityY",
            "VelocityZ",
            "VelocityMagnitude",
            "Upright",
            "Grounded",
            "Seated",
            "AFK",
            "TrackingType",
            "VRMode",
            "MuteSelf",
            "InStation",
            "Earmuffs",
            "IsOnFriendsList",
            "AvatarVersion",
            "IsAnimatorEnabled",

            "ScaleModified",
            "ScaleFactor",
            "ScaleFactorInverse",
            "EyeHeightAsMeters",
            "EyeHeightAsPercent",
        };
    }
}
