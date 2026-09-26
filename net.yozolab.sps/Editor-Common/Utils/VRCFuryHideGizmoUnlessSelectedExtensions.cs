using System.Collections.Generic;
using UnityEngine;
using YozoLab.SPS.Component;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace YozoLab.SPS.Utils {
    internal static class VRCFuryHideGizmoUnlessSelectedExtensions {
        public static void Hide(VFGameObject root, ISet<UnityEngine.Component> ignore = null) {
            foreach (var c in root.GetComponentsInSelfAndChildren()) {
                if (ignore != null && ignore.Contains(c)) continue;
                if (c.owner().GetComponent<SpsHideGizmoUnlessSelected>() != null) continue;
                if (c is ContactBase || c is Light || c is VRCPhysBone || c is VRCPhysBoneCollider) {
                    c.owner().AddComponent<SpsHideGizmoUnlessSelected>();
                }
            }
        }
    }
}
