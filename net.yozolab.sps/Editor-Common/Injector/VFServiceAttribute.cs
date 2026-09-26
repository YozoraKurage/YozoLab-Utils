using System;
using JetBrains.Annotations;

namespace YozoLab.SPS.Injector {
    [AttributeUsage(AttributeTargets.Class)]
    [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
    internal class VFServiceAttribute : Attribute {
        
    }
}
