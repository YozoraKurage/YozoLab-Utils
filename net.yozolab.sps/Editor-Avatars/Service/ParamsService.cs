using YozoLab.SPS.Builder;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YozoLab.SPS.Service {
    [VFService]
    /**
     * SPS が使う Expression Parameters。
     *
     * VRCFury はアバターのものを複製して書き足し、重複も取り除いていたが、SPS では SPS の分だけを
     * 集め、最後に Modular Avatar の Parameters として宣言する（Ndmf/SpsOutput）。
     * アバターの既存のものは読むだけ。
     */
    internal class ParamsService {
        [VFAutowired] private readonly VRCAvatarDescriptor avatar;
        
        private ParamManager _params;
        public ParamManager GetParams() {
            if (_params == null) {
                var prms = VrcfObjectFactory.Create<VRCExpressionParameters>();
                prms.parameters = new VRCExpressionParameters.Parameter[] { };
                _params = new ParamManager(prms);
            }
            return _params;
        }

        public void ClearCache() {
            _params = null;
        }

        public VRCExpressionParameters GetReadOnlyParams() {
            var p = VRCAvatarUtils.GetAvatarParams(avatar);
            if (p == null) {
                p = VrcfObjectFactory.Create<VRCExpressionParameters>();
                p.parameters = new VRCExpressionParameters.Parameter[] { };
            }
            return p;
        }
    }
}
