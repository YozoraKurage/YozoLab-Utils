using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Utils;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Preview {
    /**
     * NDMF のプレビューで、SPS の変形をシーンに出す。
     *
     * ビルドした VRChat のアバターと同じ仕組み（Socket の目印・Plug の Resolver が画面にデータを書き、
     * 加工済みのシェーダーがそれを読んで曲がる）を、元のアバターに触れずに組む。
     * - Plug のメッシュ：NDMF が作るプレビュー用の複製のマテリアルを、SPS 加工済みのコピーに替える
     * - Resolver：隠しオブジェクトとして作り、Plug に毎フレーム付いて行かせる
     * - Socket の目印：SpsPreviewSockets がシーンの全 Socket について置く
     * アバターではアニメーションで入る値（ID など）は、ここで直接入れる。
     *
     * 画面のデータは VRChat と同じく HDR（浮動小数）のバッファを前提にしている。
     * シーンビューのカメラが HDR でない（プロジェクトのグラフィック設定で HDR が無効など）と曲がらない。
     */
    internal class SpsPreviewFilter : IRenderFilter {
        public static readonly TogglablePreviewNode Toggle = TogglablePreviewNode.Create(
            () => "SPS Deformation",
            qualifiedName: "net.yozolab.sps/SpsPreviewFilter",
            initialState: true
        );

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes() {
            yield return Toggle;
        }

        public bool IsEnabled(ComputeContext context) {
            return context.Observe(Toggle.IsEnabled);
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context) {
            var groups = new List<RenderGroup>();
            var claimed = new HashSet<Renderer>();
            foreach (var root in context.GetAvatarRoots()) {
                if (!context.ActiveInHierarchy(root)) continue;
                foreach (var plug in context.GetComponentsInChildren<SpsPlug>(root, true)) {
                    // 設定の変更で作り直す
                    context.Observe(plug, p => EditorJsonUtility.ToJson(p));
                    if (!plug.enableSps) continue;
                    ICollection<Renderer> renderers;
                    try {
                        renderers = SpsPlugEditor.GetRenderers(plug);
                    } catch (Exception) {
                        continue;
                    }
                    var targets = renderers.Where(r => r != null && claimed.Add(r)).ToList();
                    if (targets.Count == 0) continue;
                    foreach (var r in targets) context.Observe(r, r2 => r2.sharedMaterials.Length);
                    groups.Add(RenderGroup.For(targets).WithData(plug, (a, b) => a == b));
                }
            }
            return groups.ToImmutableList();
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context
        ) {
            var plug = group.GetData<SpsPlug>();
            PlugNode node;
            try {
                node = PlugNode.Create(plug);
            } catch (Exception e) {
                Debug.LogWarning($"YozoLab SPS preview: skipped plug {plug.owner().GetDebugPath()}: {e.Message}");
                node = PlugNode.Empty();
            }
            return Task.FromResult<IRenderFilterNode>(node);
        }

        private class PlugNode : IRenderFilterNode {
            private SpsPreviewRig rig;
            private IDisposable socketsLease;
            private readonly Dictionary<Renderer, Material[]> materials = new Dictionary<Renderer, Material[]>();
            private readonly Dictionary<Renderer, int> vertexCounts = new Dictionary<Renderer, int>();
            private readonly List<Object> owned = new List<Object>();
            private float worldLength;

            public RenderAspects WhatChanged => RenderAspects.Material;

            public static PlugNode Empty() => new PlugNode();

            /** SpsPlugBaker.Bake のうち、描画に要る部分だけを、元に触れずに再現する */
            public static PlugNode Create(SpsPlug plug) {
                var node = new PlugNode();
                try {
                    node.Build(plug);
                } catch {
                    node.Dispose();
                    throw;
                }
                return node;
            }

            private void Build(SpsPlug plug) {
                if (!BuildTargetUtils.IsDesktop()) return;
                var transform = plug.owner();
                if (!HapticUtils.AssertValidScale(transform, "plug", shouldThrow: false)) return;

                var injector = VRCFuryPerFrameInjector.GetPerFrameInjector(transform);
                var spsMarkers = injector.GetService<SpsMarkersService>();
                var spsConfigurer = injector.GetService<SpsConfigurer>();

                var size = PlugSizeDetector.GetWorldSize(plug);
                worldLength = size.worldLength;
                var resolverHash = spsMarkers.NewMarkerId();

                rig = new SpsPreviewRig("YozoLab SPS Preview (Plug)");
                var (localSpace, oneSpace) = rig.Follow(plug.name, transform, size.localPosition, size.localRotation);
                VFGameObject bakeRoot = localSpace;

                var bakeInputs = new List<SpsBaker.RendererBakeInput>();
                foreach (var renderer in size.renderers) {
                    var spsBlendshapes = plug.spsBlendshapes
                        .Where(renderer.HasBlendshape)
                        .Distinct()
                        .Take(16)
                        .ToArray();
                    var activeFromMask = PlugMaskGenerator.GetMask(renderer, plug);
                    bakeInputs.Add(new SpsBaker.RendererBakeInput { renderer = renderer, activeFromMask = activeFromMask });

                    var baked = SpsBaker.Bake(renderer, localSpace, activeFromMask, false, spsBlendshapes);
                    baked.hideFlags = HideFlags.HideAndDontSave;
                    owned.Add(baked);

                    var slots = size.matSlots.Get(renderer.owner());
                    var source = renderer.sharedMaterials;
                    var patched = new Material[source.Length];
                    var copies = new Dictionary<Material, Material>();
                    for (var slot = 0; slot < source.Length; slot++) {
                        var mat = source[slot];
                        if (mat == null || !slots.Contains(slot)) continue;
                        if (!copies.TryGetValue(mat, out var copy)) {
                            copy = new Material(mat) { name = mat.name + " (SPS preview)", hideFlags = HideFlags.HideAndDontSave };
                            owned.Add(copy);
                            SpsConfigurer.ConfigureSpsMaterial(renderer, copy, worldLength, baked,
                                plug, bakeRoot, spsBlendshapes, resolverHash);
                            copies[mat] = copy;
                        }
                        patched[slot] = copy;
                    }
                    materials[renderer] = patched;
                    vertexCounts[renderer] = VertexCount(renderer);
                }

                var resolver = SpsPreviewRig.CreateMarkerRenderer(
                    "SpsResolver", oneSpace, SpsMarkersService.ResolverShaderName);
                SpsEditPreviewProperties.Apply(spsConfigurer.GetResolverProperties(
                    resolver,
                    worldLength,
                    size.worldRadius,
                    SpsBaker.GetPackedResolverRadiusSamples(bakeInputs, localSpace, worldLength),
                    MetadataColor(size.renderers),
                    resolverHash,
                    plug
                ));
                rig.Seal();

                socketsLease = SpsPreviewSockets.Acquire();
            }

            /** ビルドでは SpsColorSampler が描画して色を取るが、プレビューではマテリアルの色で足りる */
            private static Color MetadataColor(IEnumerable<Renderer> renderers) {
                var mat = renderers.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.HasProperty("_Color"));
                return mat != null ? mat.GetColor("_Color") : Color.white;
            }

            private static int VertexCount(Renderer renderer) {
                switch (renderer) {
                    case SkinnedMeshRenderer skin:
                        return skin.sharedMesh != null ? skin.sharedMesh.vertexCount : -1;
                    default:
                        var filter = renderer.GetComponent<MeshFilter>();
                        return filter != null && filter.sharedMesh != null ? filter.sharedMesh.vertexCount : -1;
                }
            }

            public void OnFrameGroup() {
                rig?.Sync();
            }

            public void OnFrame(Renderer original, Renderer proxy) {
                if (original == null || proxy == null) return;
                if (!materials.TryGetValue(original, out var patched)) return;
                // 手前のプレビューがメッシュの頂点を変えていると、焼いた頂点番号と合わなくなる
                if (VertexCount(proxy) != vertexCounts[original]) return;

                var current = proxy.sharedMaterials;
                var changed = false;
                for (var i = 0; i < current.Length && i < patched.Length; i++) {
                    if (patched[i] == null || current[i] == patched[i]) continue;
                    current[i] = patched[i];
                    changed = true;
                }
                if (changed) proxy.sharedMaterials = current;

                // 曲がった先が画面外判定で消えないよう、境界を Plug の長さぶん広げる
                if (proxy is SkinnedMeshRenderer skin) {
                    var bounds = skin.localBounds;
                    var scale = skin.rootBone != null ? skin.rootBone.lossyScale.x : skin.transform.lossyScale.x;
                    var pad = worldLength * 2 / Mathf.Max(Mathf.Abs(scale), 1e-6f);
                    if (bounds.extents.x < pad || bounds.extents.y < pad || bounds.extents.z < pad) {
                        bounds.Expand(pad);
                        skin.localBounds = bounds;
                    }
                }
            }

            public void Dispose() {
                socketsLease?.Dispose();
                socketsLease = null;
                rig?.Destroy();
                rig = null;
                foreach (var o in owned) if (o != null) Object.DestroyImmediate(o);
                owned.Clear();
                materials.Clear();
            }
        }
    }
}
