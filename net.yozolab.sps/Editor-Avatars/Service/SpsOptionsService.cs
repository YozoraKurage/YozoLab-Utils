using System.Linq;
using UnityEngine;
using YozoLab.SPS.Component;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.Feature;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Service {
    [VFService]
    internal class SpsOptionsService {
        [VFAutowired] private readonly VFGameObject avatarObject;
        [VFAutowired] private readonly GlobalsService globals;
        [VFAutowired] private readonly MenuService menuService;

        /**
         * SPS のメニューにアイコンを付ける。
         * VRCFury では、既存のメニューにある古い "Holes" / "Sockets" を SPS の下へ移す処理も
         * していたが、ユーザーのメニューを動かすことになるので持ち込まない。
         */
        [FeatureBuilderAction(FeatureOrder.MoveSpsMenus)]
        public void MoveMenus() {
            var menu = menuService.GetMenu();
            var mainPath = GetMenuPath();

            var icon = GetOptions().menuIcon?.Get()
                       ?? VRCFuryEditorUtils.GetResource<Texture2D>("sps_icon.png");
            menu.SetIcon(mainPath, icon);

            var optionsPath = GetOptionsPath();
            if (optionsPath != mainPath) {
                var optionsIcon = VRCFuryEditorUtils.LoadGuid<Texture2D>("16e0846165acaa1429417e757c53ef9b");
                if (optionsIcon != null) {
                    menu.SetIcon(optionsPath, optionsIcon);
                }
            }
        }

        public SpsOptions GetOptions() {
            var opts = globals.allFeaturesInRun.OfType<SpsOptions>().FirstOrDefault();
            return opts ?? new SpsOptions();
        }

        public string GetMenuPath() {
            var path = GetOptions().menuPath;
            if (string.IsNullOrWhiteSpace(path)) {
                path = "SPS";
            }
            return path;
        }

        public string GetOptionsPath() {
            if (!avatarObject.GetComponentsInSelfAndChildren<SpsSocket>().Any(s => s.addMenuItem)) {
                return GetMenuPath();
            }
            return GetMenuPath() + "/<b>Options";
        }
    }
}
