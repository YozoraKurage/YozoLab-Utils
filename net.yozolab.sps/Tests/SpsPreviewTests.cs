using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using YozoLab.SPS.Component;
using YozoLab.SPS.Preview;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Tests {
    /**
     * NDMF プレビューで SPS の変形を見せる仕組み（SpsPreviewFilter）。
     *
     * 元のアバターに触れないこと、Plug のマテリアルと Resolver・Socket の目印の値が
     * ビルドと同じ繋がり方をしていること、後片付けが済むことを確かめる。
     */
    [Category("YozoLab SPS")]
    public class SpsPreviewTests {
        private const string TempDir = "Assets/SpsPreviewTestsTemp";
        private readonly List<Object> cleanup = new List<Object>();
        private readonly List<IRenderFilterNode> nodes = new List<IRenderFilterNode>();

        [SetUp]
        public void SetUp() {
            if (!AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.CreateFolder("Assets", "SpsPreviewTestsTemp");
        }

        [TearDown]
        public void TearDown() {
            foreach (var n in nodes) n.Dispose();
            nodes.Clear();
            foreach (var o in cleanup) if (o != null) Object.DestroyImmediate(o);
            cleanup.Clear();
            AssetDatabase.DeleteAsset(TempDir);
        }

        private GameObject Make(string name) {
            var go = new GameObject(name);
            cleanup.Add(go);
            return go;
        }

        /** +Z 向き・長さ 0.2m の円柱を Plug に */
        private (SpsPlug plug, MeshRenderer renderer, Material material) MakePlug() {
            var plugObj = Make("Plug");
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            cylinder.transform.SetParent(plugObj.transform, false);
            cylinder.transform.localRotation = Quaternion.Euler(90, 0, 0);
            cylinder.transform.localScale = Vector3.one * 0.1f;
            cylinder.transform.localPosition = new Vector3(0, 0, 0.1f);
            var material = new Material(Shader.Find("Standard")) { color = Color.red };
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath($"{TempDir}/Plug.mat"));
            var renderer = cylinder.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var plug = plugObj.AddComponent<SpsPlug>();
            plug.autoRenderer = false;
            plug.configureTpsMesh.Add(renderer);
            return (plug, renderer, material);
        }

        private SpsSocket MakeSocket(Vector3 position, Quaternion rotation) {
            var obj = Make("Socket");
            obj.transform.SetPositionAndRotation(position, rotation);
            var socket = obj.AddComponent<SpsSocket>();
            socket.addLight = SpsSocket.AddLight.Hole;
            return socket;
        }

        /** NDMF がプレビュー用に作る複製の代わり */
        private MeshRenderer MakeProxy(MeshRenderer original) {
            var proxy = Object.Instantiate(original.gameObject, original.transform.parent);
            proxy.name = original.name + " (proxy)";
            cleanup.Add(proxy);
            return proxy.GetComponent<MeshRenderer>();
        }

        private IRenderFilterNode Instantiate(SpsPlug plug, Renderer original, Renderer proxy) {
            var filter = new SpsPreviewFilter();
            var group = RenderGroup.For(original).WithData(plug, (a, b) => a == b);
            var node = filter.Instantiate(group, new[] { (original, proxy) }, ComputeContext.NullContext).Result;
            nodes.Add(node);
            node.OnFrameGroup();
            node.OnFrame(original, proxy);
            return node;
        }

        private static GameObject[] PreviewRoots() {
            return Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(o => o.name.StartsWith("YozoLab SPS Preview"))
                .ToArray();
        }

        private static float BlockFloat(Renderer renderer, string name) {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetFloat(name);
        }

        [Test]
        public void Filter_TargetsTheRenderersOfEachEnabledPlug() {
            var avatar = Make("Avatar");
            avatar.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            var (plug, renderer, _) = MakePlug();
            plug.transform.SetParent(avatar.transform, false);
            var (disabled, disabledRenderer, _) = MakePlug();
            disabled.transform.SetParent(avatar.transform, false);
            disabled.enableSps = false;

            // NDMF はシーンの変化を次の更新で拾うので、ここでは手で古い結果を捨てる
            PropCacheDebug.InvalidateAllCaches();
            var groups = new SpsPreviewFilter().GetTargetGroups(new ComputeContext("test"));

            var group = groups.Single(g => g.Renderers.Contains(renderer));
            Assert.AreSame(plug, group.GetData<SpsPlug>());
            Assert.IsFalse(groups.Any(g => g.Renderers.Contains(disabledRenderer)), "SPS deformation is off");
        }

        /** NDMF のプレビューが実際にこのフィルターを動かすか（プロキシのマテリアルが替わる） */
        [UnityTest]
        public IEnumerator Ndmf_RunsTheFilterInItsPreviewSession() {
            var avatar = Make("Avatar");
            avatar.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            var (plug, renderer, _) = MakePlug();
            plug.transform.SetParent(avatar.transform, false);
            MakeSocket(new Vector3(0, 0, 0.3f), Quaternion.identity);

            // NDMF はカメラの描画の直前にプロキシを用意するので、描画できる環境でしか確かめられない
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            var camera = Make("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0.6f, 0, 0.12f);
            camera.transform.LookAt(new Vector3(0, 0, 0.12f));
            camera.targetTexture = new RenderTexture(256, 256, 24);
            cleanup.Add(camera.targetTexture);

            Renderer proxy = null;
            for (var frame = 0; frame < 300 && proxy == null; frame++) {
                yield return null;
                camera.Render();
                proxy = Resources.FindObjectsOfTypeAll<Renderer>()
                    .Where(r => r != renderer && r.sharedMaterials.Any(m => m != null && m.shader.name.StartsWith("Hidden/YozoLabSPSPatched/")))
                    .FirstOrDefault(r => (r.hideFlags & HideFlags.DontSave) != 0);
            }
            Assert.IsNotNull(proxy, "a proxy renderer with the SPS patched material");
            Assert.That(PreviewRoots().Select(r => r.name), Is.EquivalentTo(new[] {
                "YozoLab SPS Preview (Plug)", "YozoLab SPS Preview (Sockets)"
            }));

            // アバターが対象から外れれば、NDMF がフィルターの後片付けを呼び、隠しオブジェクトも消える
            // （NDMF は Undo に載った変更で気付くので、ユーザーの操作と同じく Undo を通す）
            Undo.RecordObject(avatar, "Disable avatar");
            avatar.SetActive(false);
            for (var frame = 0; frame < 300 && PreviewRoots().Length > 0; frame++) {
                yield return null;
                camera.Render();
            }
            Assert.IsEmpty(PreviewRoots(), "cleaned up after the avatar is disabled");
        }

        [Test]
        public void Preview_LeavesTheAvatarUntouched() {
            var (plug, renderer, material) = MakePlug();
            var socket = MakeSocket(new Vector3(0, 0, 0.3f), Quaternion.identity);
            var proxy = MakeProxy(renderer);
            var plugChildren = plug.transform.childCount;

            Instantiate(plug, renderer, proxy);

            Assert.AreEqual(plugChildren, plug.transform.childCount, "nothing is added under the plug");
            Assert.AreEqual(0, socket.transform.childCount, "nothing is added under the socket");
            Assert.AreSame(material, renderer.sharedMaterial, "the original renderer keeps its material");
            Assert.AreEqual("Standard", material.shader.name, "the material asset itself is not patched");
            Assert.IsNotNull(renderer.GetComponent<MeshRenderer>(), "the renderer is not replaced");
            Assert.IsFalse(EditorUtility.IsDirty(material));

            var roots = PreviewRoots();
            Assert.That(roots.Select(r => r.name), Is.EquivalentTo(new[] {
                "YozoLab SPS Preview (Plug)", "YozoLab SPS Preview (Sockets)"
            }));
            foreach (var t in roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))) {
                Assert.AreEqual(HideFlags.HideAndDontSave, t.gameObject.hideFlags, t.name + " is hidden and not saved");
            }
        }

        [Test]
        public void Preview_WiresThePlugToItsResolverAndTheSocketMarkers() {
            var (plug, renderer, _) = MakePlug();
            MakeSocket(new Vector3(0, 0, 0.3f), Quaternion.identity);
            var proxy = MakeProxy(renderer);

            Instantiate(plug, renderer, proxy);

            var patched = proxy.sharedMaterial;
            StringAssert.StartsWith("Hidden/YozoLabSPSPatched/", patched.shader.name);
            Assert.AreEqual(1, patched.GetFloat("_SPS_Configured"));
            Assert.IsNotNull(patched.GetTexture("_SPS_Bake"));

            var markers = PreviewRoots().SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)).ToList();
            var resolver = markers.Single(r => r.name == "SpsResolver");
            Assert.AreEqual(1, BlockFloat(resolver, "_SPS_Configured"));
            Assert.AreEqual(patched.GetFloat("_SPS_IdLow"), BlockFloat(resolver, "_SPS_IdLow"), "same ID as the plug");
            Assert.AreEqual(patched.GetFloat("_SPS_IdHigh"), BlockFloat(resolver, "_SPS_IdHigh"), "same ID as the plug");
            Assert.AreEqual(0.2f, BlockFloat(resolver, "_SPS_BakedLength"), 0.001f);
            Assert.AreEqual(Vector3.one, resolver.transform.localScale, "the scale that the animator would restore");

            var marker = markers.Single(r => r.name == "SpsScreenMarker");
            Assert.AreEqual(1, BlockFloat(marker, "_SPS_Configured"));
            Assert.AreEqual(1, BlockFloat(marker, "_SPS_SocketHole"));
            Assert.AreEqual(Vector3.one, marker.transform.localScale);
            Assert.AreEqual(new Vector3(0, 0, 0.3f), marker.transform.position);
        }

        [Test]
        public void Preview_FollowsThePlugAndSocket() {
            var (plug, renderer, _) = MakePlug();
            var socket = MakeSocket(new Vector3(0, 0, 0.3f), Quaternion.identity);
            var proxy = MakeProxy(renderer);
            var node = Instantiate(plug, renderer, proxy);
            var markers = PreviewRoots().SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)).ToList();
            var resolver = markers.Single(r => r.name == "SpsResolver");
            var marker = markers.Single(r => r.name == "SpsScreenMarker");

            plug.transform.position = new Vector3(1, 2, 3);
            plug.transform.rotation = Quaternion.Euler(0, 90, 0);
            socket.transform.position = new Vector3(-1, 0, 0);
            node.OnFrameGroup();
            SpsPreviewSockets.SyncForTests();

            Assert.That(Vector3.Distance(new Vector3(1, 2, 3), resolver.transform.position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(plug.transform.rotation, resolver.transform.rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(new Vector3(-1, 0, 0), marker.transform.position), Is.LessThan(1e-4f));

            plug.gameObject.SetActive(false);
            node.OnFrameGroup();
            Assert.IsFalse(resolver.gameObject.activeInHierarchy, "hidden with the plug");
        }

        [Test]
        public void Preview_CleansUpWhenDisposed() {
            var (plug, renderer, _) = MakePlug();
            MakeSocket(new Vector3(0, 0, 0.3f), Quaternion.identity);
            var node = Instantiate(plug, renderer, MakeProxy(renderer));
            var patched = PreviewRoots().Length;
            Assert.AreEqual(2, patched);

            node.Dispose();
            nodes.Clear();

            Assert.IsEmpty(PreviewRoots(), "no hidden objects are left behind");
        }

        [Test]
        public void Preview_SkipsAProxyWhoseMeshNoLongerMatches() {
            var (plug, renderer, material) = MakePlug();
            var proxy = MakeProxy(renderer);
            var node = Instantiate(plug, renderer, proxy);
            proxy.sharedMaterial = material;

            // 手前のプレビュー（メッシュの削除など）で頂点数が変わった
            var smaller = new Mesh {
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.right },
                triangles = new[] { 0, 1, 2 }
            };
            cleanup.Add(smaller);
            proxy.GetComponent<MeshFilter>().sharedMesh = smaller;
            node.OnFrame(renderer, proxy);

            Assert.AreSame(material, proxy.sharedMaterial);
        }

        /**
         * 実際に描いて、Plug が Socket の方へ曲がるか。
         * 描画できない環境（-nographics）ではスキップする。結果の画像は Logs/SpsPreview/ に出る。
         */
        [Test]
        public void Render_PlugBendsTowardANearbySocket() {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");

            var straight = RenderWithSocket(new Vector3(0, 5, 5), Quaternion.identity, "straight");
            var bent = RenderWithSocket(
                new Vector3(0, 0.08f, 0.24f),
                Quaternion.LookRotation(new Vector3(0, -0.4f, -1f)),
                "bent");
            var s = RedRows(straight);
            var b = RedRows(bent);
            Debug.Log($"[SPS PREVIEW] straight rows {s.min}..{s.max}, bent rows {b.min}..{b.max}");
            Assert.Greater(s.max, s.min, "the plug should be visible");
            Assert.Greater(b.max, s.max + 5, "the plug should bend up toward the socket");
        }

        private Color32[] RenderWithSocket(Vector3 socketPosition, Quaternion socketRotation, string name) {
            var (plug, renderer, _) = MakePlug();
            MakeSocket(socketPosition, socketRotation);
            var proxy = MakeProxy(renderer);
            renderer.enabled = false;
            var node = Instantiate(plug, renderer, proxy);

            var light = Make("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.LookRotation(new Vector3(-1, -0.5f, 0.2f));
            var camera = Make("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0.6f, 0, 0.12f);
            camera.transform.LookAt(new Vector3(0, 0, 0.12f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = 40;
            camera.nearClipPlane = 0.01f;
            camera.allowHDR = true;
            // VRChat と同じく浮動小数のバッファ（8bit ではデータが壊れる）
            var rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGBHalf);
            camera.targetTexture = rt;
            // SPS は前のフレームの画面を読むわけではないが、念のため二度描く
            camera.Render();
            camera.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            var pixels = tex.GetPixels32();
            Directory.CreateDirectory("Logs/SpsPreview");
            File.WriteAllBytes($"Logs/SpsPreview/{name}.png", tex.EncodeToPNG());

            camera.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            node.Dispose();
            nodes.Remove(node);
            foreach (var o in cleanup) if (o != null) Object.DestroyImmediate(o);
            cleanup.Clear();
            return pixels;
        }

        private static (int min, int max) RedRows(Color32[] pixels) {
            int min = int.MaxValue, max = int.MinValue;
            for (var i = 0; i < pixels.Length; i++) {
                var p = pixels[i];
                if (p.r > 60 && p.r > p.g * 2 && p.r > p.b * 2) {
                    min = Mathf.Min(min, i / 1024);
                    max = Mathf.Max(max, i / 1024);
                }
            }
            return (min, max);
        }
    }
}
