using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YozoLab.SPS.Component;
using YozoLab.SPS.Exceptions;
using YozoLab.SPS.Hooks.UnityFixes;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Builder.Haptics {
    internal static class SpsDetachedBakeAndSave {
        public static void Run(
            IList<SpsSocket> sockets,
            IList<SpsPlug> plugs,
            Action<SpsSocket, SpsSocketEditor.BakeResult> onSocketBaked = null,
            Action<SpsPlug, SpsPlugEditor.BakeResult> onPlugBaked = null
        ) {
            if (sockets.Count == 0 && plugs.Count == 0) return;

            var injector = new VRCFuryInjector();
            injector.ImportScan(typeof(VFServiceAttribute));
            var plugBaker = injector.GetService<SpsPlugBaker>();
            var socketBaker = injector.GetService<SpsSocketBaker>();
            var saveSession = new SaveAssetsSession("SPS");

            foreach (var socket in sockets) {
                socket.Upgrade();
                try {
                    var result = socketBaker.Bake(socket);
                    if (result == null) continue;
                    onSocketBaked?.Invoke(socket, result);
                    SpsConfigurer.AddMaterialPropertyAnimator(
                        result.screenMarkerResults.Select(marker => marker.materialProperties).SelectMany(properties => properties),
                        saveSession
                    );
                    Unity6RendererFixHook.Register(result.bakeRoot);
                    foreach (var component in result.bakeRoot.GetComponentsInSelfAndChildren<UnityEngine.Component>()) {
                        saveSession.SaveAssetAndChildren(component);
                    }
                    foreach (var component in result.screenMarkers.SelectMany(marker => marker.GetComponentsInSelfAndChildren<UnityEngine.Component>())) {
                        saveSession.SaveAssetAndChildren(component);
                    }
                    VRCFuryHideGizmoUnlessSelectedExtensions.Hide(result.bakeRoot);
                } catch (Exception e) {
                    throw new ExceptionWithCause($"Failed to build SPS Socket: {socket.owner().GetDebugPath()}", e);
                } finally {
                    UnityEngine.Object.DestroyImmediate(socket);
                }
            }

            foreach (var plug in plugs) {
                plug.Upgrade();
                try {
                    var result = plugBaker.Bake(plug);
                    if (result == null) continue;
                    onPlugBaked?.Invoke(plug, result);
                    if (result.resolverMaterialProperties != null) {
                        SpsConfigurer.AddMaterialPropertyAnimator(result.resolverMaterialProperties, saveSession);
                    }
                    Unity6RendererFixHook.Register(result.bakeRoot);
                    foreach (var component in result.bakeRoot.GetComponentsInSelfAndChildren<UnityEngine.Component>()) {
                        saveSession.SaveAssetAndChildren(component);
                    }
                    foreach (var component in result.renderers.SelectMany(renderer => renderer.renderer.owner().GetComponentsInSelfAndChildren<UnityEngine.Component>())) {
                        saveSession.SaveAssetAndChildren(component);
                    }
                    VRCFuryHideGizmoUnlessSelectedExtensions.Hide(result.bakeRoot);
                } catch (Exception e) {
                    throw new ExceptionWithCause($"Failed to build SPS Plug: {plug.owner().GetDebugPath()}", e);
                } finally {
                    UnityEngine.Object.DestroyImmediate(plug);
                }
            }

            saveSession.Finish();
        }
    }
}
