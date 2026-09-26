using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YozoLab.SPS.Model;
using YozoLab.SPS.Utils;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Builder {
    internal static class VRCFPrefabFixer {
        /**
         * プレハブのインスタンス上で、SPS コンポーネントの設定が上書きされているか（インスペクタの警告用）。
         * VRCFury にはこの上書きを自動で元に戻す処理もあったが、ユーザーの編集を消すので持ち込まない。
         */
        public static ICollection<PropertyModification> GetModifications(Object obj) {
            var parents = new HashSet<Object>();
            for (var i = obj; i != null; i = GetCorrespondingObjectFromSource(i)) {
                parents.Add(i);
            }
            var mods = PrefabUtility.GetPropertyModifications(obj);
            if (mods == null) return new PropertyModification[] { };
            return mods.Where(mod => parents.Contains(mod.target)).ToArray();
        }

        private static T GetCorrespondingObjectFromSource<T>(T obj) where T : Object {
            // For some reason, this method in unity occasionally throws a random "Specified cast is not valid" exception
            try {
                return PrefabUtility.GetCorrespondingObjectFromSource(obj);
            } catch (Exception) {
                return null;
            }
        }
    }
}
