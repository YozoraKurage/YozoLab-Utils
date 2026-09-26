using UnityEngine;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Utils;
using static YozoLab.SPS.Inspector.SpsSocketEditor;

namespace YozoLab.SPS.Inspector {
    [VFService]
    internal class SpsSocketMarkerService {
        [VFAutowired] private readonly SpsMarkersService spsMarkers;
        [VFAutowired] private readonly SpsConfigurer spsConfigurer;

        public ScreenMarkerResult Create(
            VFGameObject parent,
            SpsSocket socket,
            float socketScale,
            SpsSocket.AddLight lightType,
            uint socketId,
            bool useRadiusOffset,
            bool useTangentIn = false,
            Vector3 tangentIn = default,
            bool useTangentOut = false,
            Vector3 tangentOut = default,
            uint nextSocketId = 0,
            bool includeTags = true,
            string objectName = "SpsScreenMarker"
        ) {
            if (!BuildTargetUtils.IsDesktop()) return null;
            if (lightType == SpsSocket.AddLight.None) return null;

            var screenMarker = GameObjects.Create(objectName, parent);
            screenMarker.AddComponent<MeshFilter>();
            var meshRenderer = screenMarker.AddComponent<MeshRenderer>();
            spsMarkers.ConfigureSocketRenderer(meshRenderer);
            screenMarker.AddComponent<SpsHideGizmoUnlessSelected>();
            screenMarker.AddComponent<SpsGreenScreenFix>();
            return new ScreenMarkerResult {
                obj = screenMarker,
                renderer = meshRenderer,
                materialProperties = spsConfigurer.GetSocketProperties(
                    meshRenderer, socket, socketScale, lightType, socketId, useTangentIn, tangentIn,
                    useTangentOut, tangentOut, useRadiusOffset, nextSocketId, includeTags
                )
            };
        }
    }
}
