using YozoLab.SPS.Model.Feature;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Feature.Base {
    internal abstract class FeatureBuilder {
        public VFGameObject featureBaseObject;
        public int uniqueModelNum;

        public virtual string GetClipPrefix() {
            return null;
        }
    }

    internal abstract class FeatureBuilder<ModelType> : FeatureBuilder, IVRCFuryBuilder<ModelType> where ModelType : FeatureModel {
        public ModelType model;
    }
}
