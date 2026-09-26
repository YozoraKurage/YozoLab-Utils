using System;
using JetBrains.Annotations;

namespace YozoLab.SPS.Injector {
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Constructor)]
    [MeansImplicitUse(ImplicitUseKindFlags.Assign)]
    internal class VFAutowiredAttribute : Attribute {
        
    }
}
