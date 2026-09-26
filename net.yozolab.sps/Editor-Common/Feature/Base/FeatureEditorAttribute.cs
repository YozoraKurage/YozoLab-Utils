using System;
using JetBrains.Annotations;

namespace YozoLab.SPS.Feature.Base {
    [AttributeUsage(AttributeTargets.Method)]
    [MeansImplicitUse]
    internal class FeatureEditorAttribute : Attribute {
    }
}
