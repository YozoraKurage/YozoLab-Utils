using YozoLab.SPS.Ndmf;
using JetBrains.Annotations;
using System.Collections.Generic;
using System.Linq;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;

namespace YozoLab.SPS.Service {
    /**
     * SPS の出力先のコントローラー。
     *
     * VRCFury はここでアバターのコントローラーを読み込んで直接書き換えていたが、その読み込みで
     * 遷移やレイヤー、パラメータを「修正」してしまう。SPS では常に新しい空のコントローラーに
     * 書き、最後に NDMF の仮想コントローラーへレイヤーを足す（Ndmf/SpsOutput）。
     */
    [VFService]
    internal class ControllersService {
        [VFAutowired] private readonly GlobalsService globals;
        [VFAutowired] private readonly LayerSourceService layerSourceService;
        [VFAutowired] private readonly VRCAvatarDescriptor avatar;
        [VFAutowired] private readonly ParamsService paramsService;
        [VFAutowired] private readonly ParameterSourceService parameterSourceService;
        [VFAutowired] [CanBeNull] private readonly NdmfSpsContext ndmf;
        private ParamManager paramz => paramsService.GetParams();

        private readonly Dictionary<VRCAvatarDescriptor.AnimLayerType, ControllerManager> _controllers
            = new Dictionary<VRCAvatarDescriptor.AnimLayerType, ControllerManager>();
        private bool applyBaseMask = true;
        private bool controllerLoadingAllowed;

        public ControllerManager GetController(VRCAvatarDescriptor.AnimLayerType type) {
            return _controllers.GetOrCreate(type, () => MakeController(type));
        }

        private ControllerManager MakeController(VRCAvatarDescriptor.AnimLayerType type) {
            return new ControllerManager(
                VFController.Create(),
                () => paramz,
                type,
                () => globals.currentFeatureNum,
                () => globals.currentFeatureClipPrefix,
                MakeUniqueParamName,
                layerSourceService
            );
        }

        public ControllerManager GetFx() {
            return GetController(VRCAvatarDescriptor.AnimLayerType.FX);
        }
        public ControllerManager GetAction() {
            return GetController(VRCAvatarDescriptor.AnimLayerType.Action);
        }
        public IList<ControllerManager> GetAllMutatedControllers() {
            return _controllers.Values.ToArray();
        }
        public IList<ControllerManager> GetAllUsedControllers() {
            return _controllers.Values.ToArray();
        }

        private bool IsParamUsed(string name) {
            if (paramsService.GetReadOnlyParams()?.FindParameter(name) != null) return true;
            if (paramsService.GetParams().GetRaw().FindParameter(name) != null) return true;
            if (ndmf != null && ndmf.IsExternalParamUsed(name)) return true;
            foreach (var c in GetAllUsedControllers()) {
                if (c.GetParam(name) != null) return true;
            }
            return false;
        }
        public string MakeUniqueParamName(string originalName) {
            var name = "VF" + globals.currentFeatureNum + "_" + originalName;

            int offset = 1;
            while (true) {
                var attempt = name + ((offset == 1) ? "" : offset+"");
                if (!IsParamUsed(attempt)) {
                    parameterSourceService.RecordParamSource(
                        attempt,
                        globals.currentFeatureObjectPath,
                        originalName
                    );
                    return attempt;
                }
                offset++;
            }
        }
    }
}
