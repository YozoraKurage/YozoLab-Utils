using JetBrains.Annotations;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YozoLab.SPS.Service {
    [VFService]
    /**
     * SPS のメニュー。
     *
     * VRCFury はアバターのメニューを複製して直接書き足していたが、SPS では SPS の項目だけの
     * 新しいメニューを作り、最後に Modular Avatar の Menu Installer で設置する（Ndmf/SpsOutput）。
     * 8 項目を超えたときのページ送りも MA に任せる。
     */
    internal class MenuService {
        [VFAutowired] private readonly GlobalsService globals;
        [VFAutowired] private readonly VRCAvatarDescriptor avatar;
        
        private MenuManager _menu;
        public MenuManager GetMenu() {
            if (_menu == null) {
                var menu = VrcfObjectFactory.Create<VRCExpressionsMenu>();
                menu.name = "SPS";
                _menu = new MenuManager(menu, () => globals.currentMenuSortPosition);
            }
            return _menu;
        }

        /** SPS の項目が一つでも作られたか。 */
        public bool HasMenu => _menu != null && _menu.GetRaw().controls.Count > 0;

        [CanBeNull]
        public VRCExpressionsMenu GetReadOnlyMenu() {
            return VRCAvatarUtils.GetAvatarMenu(avatar);
        }
    }
}
