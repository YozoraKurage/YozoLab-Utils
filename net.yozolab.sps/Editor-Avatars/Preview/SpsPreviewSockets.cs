using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Utils;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Preview {
    /**
     * プレビュー中、開いているシーンのすべての SPS Socket の「目印」を隠しオブジェクトとして置いておく。
     *
     * Plug のプレビューが一つでも動いている間だけ存在する（Acquire / Dispose で数える）。
     * Socket の追加・削除・設定の変更があれば作り直し、位置は描画の直前に毎回合わせる。
     */
    internal static class SpsPreviewSockets {
        private static int users;
        private static bool dirty;
        private static double lastBuild;
        private static SpsPreviewRig rig;

        public static IDisposable Acquire() {
            if (users++ == 0) Start();
            return new Lease();
        }

        private class Lease : IDisposable {
            private bool disposed;
            public void Dispose() {
                if (disposed) return;
                disposed = true;
                if (--users == 0) Stop();
            }
        }

        private static void Start() {
            dirty = true;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Undo.undoRedoPerformed += MarkDirty;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorApplication.update += Update;
            Camera.onPreCull += OnPreCull;
            AssemblyReloadEvents.beforeAssemblyReload += Teardown;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) {
                dirty = false;
                lastBuild = EditorApplication.timeSinceStartup;
                Rebuild();
            }
        }

        private static void Stop() {
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            Undo.undoRedoPerformed -= MarkDirty;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorApplication.update -= Update;
            Camera.onPreCull -= OnPreCull;
            AssemblyReloadEvents.beforeAssemblyReload -= Teardown;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Teardown();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change) {
            if (change == PlayModeStateChange.ExitingEditMode) Teardown();
            if (change == PlayModeStateChange.EnteredEditMode) MarkDirty();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => MarkDirty();
        private static void OnSceneClosed(Scene scene) => MarkDirty();

        private static void OnChangesPublished(ref ObjectChangeEventStream stream) {
            MarkDirty();
        }

        private static void MarkDirty() {
            dirty = true;
        }

        private static void Update() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!dirty) return;
            // ドラッグ中などに作り直しが続かないよう、間隔を空ける
            if (EditorApplication.timeSinceStartup - lastBuild < 0.2) return;
            dirty = false;
            lastBuild = EditorApplication.timeSinceStartup;
            Rebuild();
            SceneView.RepaintAll();
        }

        private static void OnPreCull(Camera camera) {
            rig?.Sync();
        }

        internal static void SyncForTests() {
            rig?.Sync();
        }

        private static void Teardown() {
            rig?.Destroy();
            rig = null;
        }

        internal static void Rebuild() {
            Teardown();
            rig = new SpsPreviewRig("YozoLab SPS Preview (Sockets)");
            rig.AddGreenScreenFix();
            foreach (var socket in FindSceneSockets()) {
                try {
                    AddSocket(rig, socket);
                } catch (Exception e) {
                    Debug.LogWarning($"YozoLab SPS preview: skipped socket {socket.owner().GetDebugPath()}: {e.Message}");
                }
            }
            rig.Seal();
        }

        private static SpsSocket[] FindSceneSockets() {
            return Resources.FindObjectsOfTypeAll<SpsSocket>()
                .Where(s => s != null)
                .Where(s => (s.hideFlags & HideFlags.HideInHierarchy) == 0)
                .Where(s => !EditorUtility.IsPersistent(s))
                .Where(s => s.gameObject.scene.IsValid() && s.gameObject.scene.isLoaded)
                .Where(s => !EditorSceneManager.IsPreviewScene(s.gameObject.scene))
                .ToArray();
        }

        /** SpsSocketBaker.Bake のうち、画面の目印を置く部分だけを、元に触れずに再現する */
        private static void AddSocket(SpsPreviewRig rig, SpsSocket socket) {
            if (!BuildTargetUtils.IsDesktop()) return;
            var transform = socket.owner();
            if (!HapticUtils.AssertValidScale(transform, "socket", shouldThrow: false)) return;

            var injector = VRCFuryPerFrameInjector.GetPerFrameInjector(transform);
            var spsMarkers = injector.GetService<SpsMarkersService>();
            var spsConfigurer = injector.GetService<SpsConfigurer>();
            var autoTagGenerator = injector.GetServices<SpsAutoTagGenerator>().FirstOrDefault();

            var closestBone = autoTagGenerator?.GetClosestBone(transform);
            var (lightType, localPosition, localRotation) =
                SpsSocketBaker.GetInfoFromLightsOrComponent(socket, closestBone);
            if (lightType == SpsSocket.AddLight.None) return;

            var (bakeRoot, oneSpace) = rig.Follow(socket.name, transform, localPosition, localRotation);
            var socketScale = bakeRoot.lossyScale.x;

            void AddMarker(
                Transform parent, float scale, SpsSocket.AddLight type, uint id, bool useRadiusOffset,
                bool useTangentIn, Vector3 tangentIn, bool useTangentOut, Vector3 tangentOut,
                uint nextId, bool includeTags
            ) {
                var renderer = SpsPreviewRig.CreateMarkerRenderer(
                    "SpsScreenMarker", parent, SpsMarkersService.SocketMarkerShaderName);
                SpsEditPreviewProperties.Apply(spsConfigurer.GetSocketProperties(
                    renderer, socket, scale, type, id, useTangentIn, tangentIn,
                    useTangentOut, tangentOut, useRadiusOffset, nextId, includeTags));
            }

            var stops = socket.guidedPathStops
                .Where(stop => stop != null && stop.transform != null)
                .ToList();
            if (stops.Count == 0) {
                AddMarker(oneSpace, socketScale, lightType, spsMarkers.NewMarkerId(), socket.useRadiusOffset,
                    false, Vector3.zero, false, Vector3.zero, 0, true);
                return;
            }

            // 誘導経路：入口の目印から、各中継点の目印へと順に繋ぐ（SpsSocketBaker と同じ並び）
            var pathIds = stops.Select(_ => spsMarkers.NewMarkerId()).ToList();
            var first = stops[0];
            AddMarker(oneSpace, socketScale,
                first.shrink ? SpsSocket.AddLight.Hole : SpsSocket.AddLight.RingOneWay,
                spsMarkers.NewMarkerId(), socket.useRadiusOffset,
                false, Vector3.zero, first.customizeTangentOut, first.tangentOutLocal, pathIds[0], true);
            for (var i = 0; i < stops.Count; i++) {
                var isLast = i == stops.Count - 1;
                var next = isLast ? null : stops[i + 1];
                var type = isLast
                    ? SpsSocketEditor.GetGuidedPathTerminalType(lightType)
                    : next.shrink ? SpsSocket.AddLight.Hole : SpsSocket.AddLight.RingOneWay;
                var (stopRoot, stopOneSpace) = rig.Follow(
                    $"Stop #{i + 1}", stops[i].transform, Vector3.zero, Quaternion.identity);
                AddMarker(stopOneSpace, stopRoot.lossyScale.x, type, pathIds[i], false,
                    stops[i].customizeTangentIn, stops[i].tangentInLocal,
                    next?.customizeTangentOut ?? false, next == null ? Vector3.zero : next.tangentOutLocal,
                    isLast ? 0 : pathIds[i + 1], false);
            }
        }
    }
}
