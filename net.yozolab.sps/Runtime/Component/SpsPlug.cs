using System;
using System.Collections.Generic;
using UnityEngine;
using YozoLab.SPS.Model;
using YozoLab.SPS.Model.StateAction;

namespace YozoLab.SPS.Component {
    [AddComponentMenu("YozoLab/SPS/SPS Plug")]
    internal class SpsPlug : SpsComponent {
        public bool autoRenderer = true;
        public bool autoPosition = true;
        public bool autoLength = true;
        public bool useBoneMask = true;
        public GuidTexture2d textureMask = null;
        public float length;
        public bool autoRadius = true;
        public float radius;
        public new string name;
        public bool unitsInMeters = false;
        public bool configureTps = false;
        public bool enableSps = true;
        [NonSerialized] public bool fromSpsForAll = false;
        public bool spsAutorig = true;
        public List<string> spsBlendshapes = new List<string>();
        public List<Renderer> configureTpsMesh = new List<Renderer>();
        public float spsAnimatedEnabled = 1;
        public bool useLegacyRendererFinder = false;
        public bool addDpsTipLight = false;
        [DoNotApplyRestingState]
        public State postBakeActions;
        public bool spsOverrun = true;
        [Obsolete] public bool enableDepthAnimations = false;
        [Obsolete] public List<LegacyPlugDepthAction> depthActions = new List<LegacyPlugDepthAction>();
        public List<SpsSocket.DepthActionNew> depthActions2 = new List<SpsSocket.DepthActionNew>();
        public bool useHipAvoidance = true;
        public bool useSharedTag = true;
        public bool useLights = true;
        public List<TagRule> includeTags = new List<TagRule>();
        public List<TagRule> excludeTags = new List<TagRule>();

        [Obsolete] public bool configureSps = false;
        [Obsolete] public bool spsBoneMask = true;
        [Obsolete] public GuidTexture2d spsTextureMask = null;
        [Obsolete] public GuidTexture2d configureTpsMask = null;
        
        [Serializable]
        [Obsolete]
        public class LegacyPlugDepthAction {
            public State state;
            public float startDistance = 1;
            public float endDistance;
            public bool enableSelf;
            public float smoothingSeconds = 0;
            [Obsolete] public float smoothing;
        }

        [Serializable]
        public class TagRule {
            public string tag;
            public bool allowSelf = true;
            public bool allowOthers = true;
        }

        public override bool Upgrade(int fromVersion) {
#pragma warning disable 0612
            if (fromVersion < 1) { 
                unitsInMeters = true;
            }
            if (fromVersion < 2) {
                autoRenderer = configureTpsMesh == null || configureTpsMesh.Count == 0;
                autoLength = length == 0;
                autoRadius = radius == 0;
            }
            if (fromVersion < 3) {
                enableSps = configureSps;
            }
            if (fromVersion < 5) {
                if (enableSps) {
                    useBoneMask = spsBoneMask;
                    textureMask = spsTextureMask;
                } else if (configureTps) {
                    useBoneMask = false;
                    textureMask = configureTpsMask;
                } else {
                    useBoneMask = false;
                }
            }
            if (fromVersion < 6) {
                useLegacyRendererFinder = !enableSps;
            }
            if (fromVersion < 7) {
                foreach (var a in depthActions) {
                    a.smoothing = (float)Math.Pow(a.smoothing, 0.2);
                }
            }
            if (fromVersion < 8) {
                foreach (var a in depthActions) {
                    a.smoothingSeconds = SpsSocket.UpgradeFromLegacySmoothing(a.smoothing);
                }
            }
            if (fromVersion < 9) {
                enableDepthAnimations = depthActions.Count > 0;
            }
            if (fromVersion < 10) {
                if (enableDepthAnimations) {
                    foreach (var a in depthActions) {
                        depthActions2.Add(new SpsSocket.DepthActionNew() {
                            actionSet = a.state,
                            enableSelf = a.enableSelf,
                            range = new Vector2(
                                Math.Min(a.startDistance, a.endDistance) - 1,
                                Math.Max(a.startDistance, a.endDistance) - 1
                            ),
                            smoothingSeconds = a.smoothingSeconds,
                            units = SpsSocket.DepthActionUnits.Plugs,
                            reverseClip = a.startDistance < a.endDistance
                        });
                    }
                }
                depthActions.Clear();
            }
#pragma warning restore 0612
            return false;
        }

        public override int GetLatestVersion() {
            return 10;
        }
    }
}
