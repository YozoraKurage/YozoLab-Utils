using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Utils {
    internal static class VrcfObjectFactory {
        private static readonly HashSet<Object> created = new HashSet<Object>();
        private static readonly HashSet<Object> doNotReuse = new HashSet<Object>();

        [VFInit]
        private static void OnLoad() {
            Scheduler.Schedule(Prune, 0);
        }

        /**
         * 記録を消すだけ。VRCFury はここで「ディスクに保存されていない、自分が作ったオブジェクト」を
         * 破棄していたが、SPS は生成物をメモリ上に作って NDMF に任せている（ビルドでは NDMF が保存し、
         * プレイモードではメモリ上のまま使われる）。破棄すると、プレイモードのアバターが参照する
         * マテリアルやコントローラーが消えてしまうので、寿命は NDMF と Unity に任せる。
         */
        private static void Prune() {
            created.Clear();
            doNotReuse.Clear();
        }

        public static T Create<T>(Object copyWorkLogFrom = null) where T : Object {
            return Create(typeof(T), copyWorkLogFrom) as T;
        }
        public static Object Create(Type type, Object copyWorkLogFrom = null) {
            Object obj;
            if (typeof(ScriptableObject).IsAssignableFrom(type)) {
                obj = ScriptableObject.CreateInstance(type) as Object;
            } else {
                obj = Activator.CreateInstance(type) as Object;
            }
            if (obj == null) {
                throw new Exception("Failed to create instance of Object " + type.FullName);
            }

            Register(obj, copyWorkLogFrom);
            return obj;
        }

        public static Material CreateMaterial(Shader shader, Object copyWorkLogFrom = null) {
            var obj = new Material(shader);
            return Register(obj, copyWorkLogFrom);
        }

        public static Texture2D CreateTexture2D(
            int width,
            int height,
            TextureFormat textureFormat = TextureFormat.RGBA32,
            bool mipChain = false,
            bool linear = false,
            Object copyWorkLogFrom = null
        ) {
            var obj = new Texture2D(width, height, textureFormat, mipChain, linear);
            return Register(obj, copyWorkLogFrom);
        }

        public static T Register<T>(T obj, Object copyWorkLogFrom = null) where T : Object {
            created.Add(obj);
            // VRCFury は自前で保存するまで「エディタで保存しない」フラグを付けていたが、SPS の保存は
            // NDMF が行うので付けない（付いたままだと NDMF が保存できない）。
            if (copyWorkLogFrom != null) {
                obj.MarkClonedFrom(copyWorkLogFrom);
            } else {
                obj.WorkLog("Created fresh");
            }
            return obj;
        }
        
        public static T DoNotReuse<T>(T obj) where T : Object {
            doNotReuse.Add(obj);
            return obj;
        }

        public static bool DidCreate(Object obj) {
            return created.Contains(obj);
        }
        
        public static bool IsMarkedAsDoNotReuse(Object obj) {
            return doNotReuse.Contains(obj);
        }
    }
}
