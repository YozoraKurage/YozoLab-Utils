using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YozoLab.SPS.Component;
using YozoLab.SPS.Exceptions;
using YozoLab.SPS.Model;
using YozoLab.SPS.Upgradeable;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS {
    internal static class VRCFuryComponentExtensions {
        private static readonly HashSet<string> reimported = new HashSet<string>();

        /**
         * 
         * Unity doesn't try to re-deserialize assets after updating vrcfury, leaving components in a broken state.
         * If we find a broken component, schedule a reimport of it to try and resolve the issue.
         */
        private static void DelayReimport(SpsComponent c) {
            string GetPath() {
                if (c == null) return null;
                var path = AssetDatabase.GetAssetPath(c);
                if (string.IsNullOrEmpty(path)) return null;
                if (reimported.Contains(path)) return null;
                return path;
            }

            if (GetPath() == null) return;
            EditorApplication.delayCall += () => {
                if (!c.IsBroken()) return;
                var path = GetPath();
                if (path == null) return;
                reimported.Add(path);
                Debug.Log("Reimporting SPS asset that unity thinks is corrupted: " + path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            };
        }

        public static void Upgrade(this SpsComponent c) {
            var brokenMessage = c.GetBrokenMessage();
            if (brokenMessage != null) throw new VRCFBuilderException(brokenMessage);
            if (PrefabUtility.IsPartOfPrefabInstance(c)) return;
            if (IUpgradeableUtility.UpgradeRecursive(c)) {
                if (c != null) c.Dirty();
            }
        }

        public static bool IsBroken(this SpsComponent c) {
            return c.GetBrokenMessage() != null;
        }
        public static string GetBrokenMessage(this SpsComponent c) {
            if (IUpgradeableUtility.IsTooNew(c)) {
                DelayReimport(c);
                return $"This component was created with a newer version of YozoLab SPS";
            }
            
            var containsNull = new SerializedObject(c).IterateFast().Any(prop =>
                prop.propertyType == SerializedPropertyType.ManagedReference
                && prop.managedReferenceValue == null
            );

            if (containsNull) {
                DelayReimport(c);
                if (Application.unityVersion.StartsWith("2019")) {
                    if (c.unityVersion != null && c.unityVersion.StartsWith("2022")) {
                        return "This asset was created using Unity 2022, which means it cannot be used on Unity 2019";
                    } else {
                        return "This asset was probably created using Unity 2022, which means it cannot be used on Unity 2019";
                    }
                }
                return "Found a null SerializeReference";
            }
            return null;
        }
    }
}
