using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;

namespace YozoLab.SPS.Ndmf {
    /**
     * NDMF のビルドの情報を、SPS のサービスへ渡すための窓口。
     *
     * SPS の各サービスは VRCFury の注入の仕組み（VRCFuryInjector）で組み立てられるので、
     * このオブジェクトを注入しておき、必要なサービスだけが [CanBeNull] で受け取る。
     * エディタのプレビューなど NDMF の外で組み立てたときは null になる。
     */
    internal class NdmfSpsContext {
        public readonly BuildContext buildContext;
        public readonly AnimatorServicesContext animatorServices;

        /** SPS のビルドを始める前からアバターにあった Contact Receiver（ユーザー自身のもの）。 */
        public readonly HashSet<VRCContactReceiver> preexistingReceivers;

        public NdmfSpsContext(BuildContext buildContext) {
            this.buildContext = buildContext;
            animatorServices = buildContext.Extension<AnimatorServicesContext>();
            preexistingReceivers = new HashSet<VRCContactReceiver>(
                buildContext.AvatarRootObject.GetComponentsInChildren<VRCContactReceiver>(true));
        }

        public VirtualControllerContext Controllers => animatorServices.ControllerContext;

        /**
         * アバター上のコントローラー（MA Merge Animator などで後から合流するものも含む）で、
         * すでにその名前のパラメータが使われているか。SPS のパラメータ名が既存のものと
         * ぶつからないようにするために使う。
         */
        public bool IsExternalParamUsed(string name) {
            foreach (var controller in Controllers.GetAllControllers()) {
                if (controller.Parameters.ContainsKey(name)) return true;
            }
            var descriptor = buildContext.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var expressionParams = descriptor != null ? descriptor.expressionParameters : null;
            if (expressionParams != null && expressionParams.parameters != null
                && expressionParams.parameters.Any(p => p != null && p.name == name)) {
                return true;
            }
            return false;
        }

        /**
         * アバターのルートからのパスを、NDMF のパス追跡に乗った形にする。
         * SPS が作ったオブジェクトは、このあと Modular Avatar などに移動されることがあるので、
         * 生のパスではなく追跡用のパスで書いておく必要がある。
         */
        public string VirtualizePath(string path) {
            if (path == null) return null;
            if (path == "") return path;
            var root = buildContext.AvatarRootTransform;
            var obj = root.Find(path);
            if (obj == null) return path;
            return animatorServices.ObjectPathRemapper.GetVirtualPathForObject(obj);
        }

        public string VirtualPathFor(GameObject obj) {
            return animatorServices.ObjectPathRemapper.GetVirtualPathForObject(obj);
        }

        /** アバター上のすべての仮想クリップ（MA Merge Animator のものも含む）。 */
        public IEnumerable<VirtualClip> GetAllVirtualClips() {
            return Controllers.GetAllControllers()
                .SelectMany(c => c.AllReachableNodes())
                .OfType<VirtualClip>()
                .Distinct();
        }
    }
}
