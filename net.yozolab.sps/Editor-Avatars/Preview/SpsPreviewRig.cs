using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.SPS.Preview {
    /**
     * プレビューのために作る、シーンに保存されない隠しオブジェクトの置き場。
     *
     * SPS のベイクはアバターの下に子オブジェクトを足すが、プレビューでは元のアバターに一切触れない。
     * 代わりにこの隠しルートの下に同じ形の物を作り、毎フレーム元の Transform に位置を合わせる。
     */
    internal class SpsPreviewRig {
        private readonly GameObject root;
        private readonly List<Follower> followers = new List<Follower>();

        private class Follower {
            public Transform source;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Transform target;
            public Transform oneSpace;
        }

        public SpsPreviewRig(string name) {
            root = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        }

        /**
         * source の子として (localPosition, localRotation) に置いたのと同じ場所に来る物を作る。
         * 返すのは、その物（元の拡縮を持つ）と、その下の拡縮 1 の子（SPS の "OneSpace" に相当）。
         */
        public (Transform bakeRoot, Transform oneSpace) Follow(
            string name, Transform source, Vector3 localPosition, Quaternion localRotation
        ) {
            var target = new GameObject(name).transform;
            target.SetParent(root.transform, false);
            var oneSpace = new GameObject("OneSpace").transform;
            oneSpace.SetParent(target, false);
            var follower = new Follower {
                source = source,
                localPosition = localPosition,
                localRotation = localRotation,
                target = target,
                oneSpace = oneSpace
            };
            followers.Add(follower);
            Sync(follower);
            return (target, oneSpace);
        }

        public void Sync() {
            foreach (var f in followers) Sync(f);
        }

        private static void Sync(Follower f) {
            if (f.source == null || f.target == null) return;
            f.target.SetPositionAndRotation(
                f.source.TransformPoint(f.localPosition),
                f.source.rotation * f.localRotation
            );
            var scale = f.source.lossyScale;
            f.target.localScale = scale;
            f.oneSpace.localScale = new Vector3(Inverse(scale.x), Inverse(scale.y), Inverse(scale.z));
            var active = f.source.gameObject.activeInHierarchy;
            if (f.target.gameObject.activeSelf != active) f.target.gameObject.SetActive(active);
        }

        private static float Inverse(float v) => Mathf.Abs(v) < 1e-8f ? 1 : 1 / v;

        /**
         * Socket の目印・Plug の Resolver と同じ描画をする物を作る。
         * SpsMarkersService の共有マテリアルは使わず、この置き場が持つ物を作る（破棄のときに一緒に消せるように）。
         */
        public static MeshRenderer CreateMarkerRenderer(string name, Transform parent, string markerShaderName) {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = CreateTriggerMesh();
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] {
                CreateMaterial(markerShaderName),
                CreateMaterial(Builder.Haptics.SpsMarkersService.DataGrabPassShaderName)
            };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        /** 画面のうち SPS がデータを書いた区画を、書く前の色に戻す（VRCFury の Green Screen Fix と同じ） */
        public void AddGreenScreenFix() {
            var obj = new GameObject("GreenScreenFix");
            obj.transform.SetParent(root.transform, false);
            var mesh = CreateTriggerMesh();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] {
                CreateMaterial("Hidden/YozoLab/SPS/VFGridBakGrabPass"),
                CreateMaterial("Hidden/YozoLab/SPS/VFGridBakRestore")
            };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material CreateMaterial(string shaderName) {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.Exception($"Failed to find SPS shader {shaderName}");
            return new Material(shader) {
                name = shaderName,
                enableInstancing = true,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        /** SpsMarkersService の目印用メッシュと同じ形（小さな三角形・大きな境界） */
        private static Mesh CreateTriggerMesh() {
            var mesh = new Mesh {
                name = "SpsTriggerMesh",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[] {
                    new Vector3(-0.005f, -0.005f, 0),
                    new Vector3(-0.005f, 0.005f, 0),
                    new Vector3(0.005f, 0.005f, 0)
                },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10);
            return mesh;
        }

        /** 作り終えたら呼ぶ。下に足された物すべてを保存しない・見せない扱いにする。 */
        public void Seal() {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        /** 下にある物と、それが使っているメモリ上だけのマテリアル・メッシュをまとめて破棄する。 */
        public void Destroy() {
            if (root == null) return;
            var owned = new HashSet<Object>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) {
                foreach (var m in r.sharedMaterials) {
                    if (m != null && !EditorUtility.IsPersistent(m)) owned.Add(m);
                }
                var filter = r.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh)) {
                    owned.Add(filter.sharedMesh);
                }
            }
            Object.DestroyImmediate(root);
            foreach (var o in owned) Object.DestroyImmediate(o);
            followers.Clear();
        }

        public bool IsAlive => root != null;
    }
}
