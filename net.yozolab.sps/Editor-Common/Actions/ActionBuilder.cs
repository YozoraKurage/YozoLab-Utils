using UnityEngine;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;
using Action = YozoLab.SPS.Model.StateAction.Action;

namespace YozoLab.SPS.Actions {
    internal abstract class ActionBuilder<ModelType> : ActionBuilder, IVRCFuryBuilder<ModelType> where ModelType : Action {
    }

    internal abstract class ActionBuilder {
        protected static VFClip NewClip() {
            return VFClip.Create();
        }
    }
}
