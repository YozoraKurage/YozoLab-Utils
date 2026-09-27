using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YozoLab.SPS.Builder.Haptics;

namespace YozoLab.SPS.Preview {
    /**
     * ベイクで出た「アニメーションで入れる値」を、エディタで止まったまま直接反映する。
     *
     * アバターでは Animator が material.xxx / m_LocalScale.x などを書き込むが、エディタでは
     * Animator が動かない。レンダラーの値は MaterialPropertyBlock、Transform の値はそのまま入れる。
     * "_Foo.x" のようにベクトルの成分に分かれた値は、成分をまとめて一つのベクトルにする。
     */
    internal static class SpsEditPreviewProperties {
        private const string MaterialPrefix = "material.";

        public static void Apply(IEnumerable<SpsConfigurer.MaterialProperty> properties) {
            var list = (properties ?? Enumerable.Empty<SpsConfigurer.MaterialProperty>())
                .Where(p => p?.component != null)
                .ToList();

            foreach (var group in list.Where(p => p.component is Renderer).GroupBy(p => (Renderer)p.component)) {
                ApplyToRenderer(group.Key, group);
            }
            foreach (var p in list.Where(p => p.component is Transform)) {
                ApplyToTransform((Transform)p.component, p.propertyName, p.value);
            }
        }

        private static void ApplyToRenderer(Renderer renderer, IEnumerable<SpsConfigurer.MaterialProperty> properties) {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            var vectors = new Dictionary<string, Vector4>();
            foreach (var p in properties) {
                if (!p.propertyName.StartsWith(MaterialPrefix)) continue;
                var name = p.propertyName.Substring(MaterialPrefix.Length);
                var dot = name.LastIndexOf('.');
                if (dot > 0 && dot == name.Length - 2 && "xyzwrgba".IndexOf(name[dot + 1]) >= 0) {
                    var baseName = name.Substring(0, dot);
                    if (!vectors.TryGetValue(baseName, out var v)) v = InitialVector(renderer, baseName);
                    v[ComponentIndex(name[dot + 1])] = p.value;
                    vectors[baseName] = v;
                } else {
                    block.SetFloat(name, p.value);
                }
            }
            foreach (var pair in vectors) block.SetVector(pair.Key, pair.Value);
            renderer.SetPropertyBlock(block);
        }

        private static Vector4 InitialVector(Renderer renderer, string name) {
            var material = renderer.sharedMaterials.FirstOrDefault(m => m != null && m.HasProperty(name));
            return material != null ? material.GetVector(name) : Vector4.zero;
        }

        private static int ComponentIndex(char c) {
            switch (c) {
                case 'x': case 'r': return 0;
                case 'y': case 'g': return 1;
                case 'z': case 'b': return 2;
                default: return 3;
            }
        }

        private static void ApplyToTransform(Transform transform, string propertyName, float value) {
            Vector3 v;
            switch (propertyName) {
                case "m_LocalScale.x": v = transform.localScale; v.x = value; transform.localScale = v; break;
                case "m_LocalScale.y": v = transform.localScale; v.y = value; transform.localScale = v; break;
                case "m_LocalScale.z": v = transform.localScale; v.z = value; transform.localScale = v; break;
                case "m_LocalPosition.x": v = transform.localPosition; v.x = value; transform.localPosition = v; break;
                case "m_LocalPosition.y": v = transform.localPosition; v.y = value; transform.localPosition = v; break;
                case "m_LocalPosition.z": v = transform.localPosition; v.z = value; transform.localPosition = v; break;
            }
        }
    }
}
