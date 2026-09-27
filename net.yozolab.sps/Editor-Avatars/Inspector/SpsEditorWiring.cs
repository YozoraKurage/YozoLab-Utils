using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Actions;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Menu;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YozoLab.SPS.Inspector {
    /**
     * エディタ側の部品どうしのつなぎ込み（アバター向けの部分を共通部分へ渡す）。
     * アバターには何もしない。
     */
    internal static class SpsEditorWiring {
        public static VFGameObject GetAvatarRoot(this VFGameObject obj) {
            if (obj == null) return null;
            var avatars = obj.GetComponentsInSelfAndParents<VRCAvatarDescriptor>();
            if (avatars.Length > 0) return avatars.Last().owner();
            var animators = obj.GetComponentsInSelfAndParents<Animator>();
            if (animators.Length > 0) return animators.Last().owner();
            return obj.root;
        }

        public static VFGameObject GetAvatarRoot(this UnityEngine.Component c) {
            return c.owner().GetAvatarRoot();
        }

        private static bool AllowRootFeatures(VFGameObject gameObject) {
            var avatarRoot = gameObject.GetAvatarRoot();
            if (gameObject == avatarRoot) {
                return true;
            }

            return gameObject.GetSelfAndAllParents()
                .First(o => o.parent == avatarRoot)
                .GetComponentsInSelfAndChildren<UnityEngine.Component>()
                .All(c => c is SpsComponent || c is Transform);
        }

        [VFInit]
        private static void Init() {
            SpsPlugEditor.getHapticsEnabled = HapticsToggleMenuItem.Get;

            VFGameObject.getUploadRoots = obj => {
                return new[] { obj.GetAvatarRoot() };
            };

            // VRCFury はここにサポート用の診断文字列（VRCFury の設定状態）を出していた。
            // このポートでは出所だけ分かればよいので、ダイアログにだけ固定の表記を出す
            // （インスペクターには出さない。見出しの「YozoLab SPS」で足りる）。
            DialogUtils.debugLineGetter = () => "YozoLab SPS (VRCFury SPS port)";

            FeatureFinder.onInjectEditor = (gameObject, builderType, injector) => {
                var allowRootFeatures = AllowRootFeatures(gameObject);
                if (builderType.GetCustomAttribute<FeatureRootOnlyAttribute>() != null && !allowRootFeatures) {
                    throw new RenderFeatureEditorException(
                        "このコンポーネントは、Avatar Descriptor のあるルートか、SPS のコンポーネントだけが付いた子オブジェクトにしか置けない" +
                        "（配布プレハブによる悪用を防ぐため）。"
                    );
                }
                injector.Set("avatarObject", gameObject.GetAvatarRoot());
            };

            FeatureFinder.onGetBuilder = (gameObject, builderType, title) => {
                var avatarObject = gameObject.GetAvatarRoot();
                var allowRootFeatures = AllowRootFeatures(gameObject);
                if (builderType.GetCustomAttribute<FeatureRootOnlyAttribute>() != null && !allowRootFeatures) {
                    throw new Exception($"この SPS コンポーネント（{title}）はアバターのルートにしか置けないが、{gameObject.GetPath(avatarObject)} にある。");
                }
            };

            VRCFuryActionSetDrawer.renderDebugInfo = (gameObject, actionSet) => {
                var debugInfo = new VisualElement();

                var avatarObject = gameObject.GetAvatarRoot();

                var injector = new VRCFuryInjector();
                injector.ImportOne(typeof(ActionClipService));
                injector.ImportOne(typeof(ClipFactoryService));
                injector.ImportOne(typeof(VRCFObjectPathCache));
                injector.ImportOne(typeof(VRCFArmatureCache));
                injector.ImportScan(typeof(ActionBuilder));
                injector.Set("avatarObject", avatarObject);
                injector.Set("componentObject", new Func<VFGameObject>(() => avatarObject));
                injector.GetService<VRCFObjectPathCache>().Capture();
                injector.GetService<VRCFArmatureCache>().Capture();
                var mainBuilder = injector.GetService<ActionClipService>();
                var test = mainBuilder.LoadStateAdv("test", actionSet, gameObject, debugMode: true);
                var bindings = new AnimatorIterator.Clips().From(test.onClip)
                    .SelectMany(clip => clip.GetAllBindings())
                    .ToImmutableHashSet();
                var clips = new HashSet<AnimationClip>();
                UnitySerializationUtils.Iterate(actionSet, visit => {
                    if (visit.value is AnimationClip clip && clip != null) {
                        clips.Add(clip);
                    }
                    return UnitySerializationUtils.IterateResult.Continue;
                });
                var warnings =
                    VrcfAnimationDebugInfo.BuildDebugInfo(clips, bindings, gameObject);

                foreach (var warning in warnings) {
                    debugInfo.Add(warning);
                }
                return debugInfo;
            };

            VRCFuryComponentEditor.renderWarnings = (owner, warnings) => {
                var descriptors = owner.GetComponentsInSelfAndParents<VRCAvatarDescriptor>()
                    .SelectMany(descriptor => descriptor.owner().GetComponentsInSelfAndChildren<VRCAvatarDescriptor>())
                    .ToImmutableHashSet();
                var editingPrefab = UnityCompatUtils.IsEditingPrefab();
                if (!editingPrefab && !descriptors.Any()) {
                    var animators = owner.GetComponentsInSelfAndParents<Animator>();
                    if (animators.Any()) {
                        warnings.Add(VRCFuryEditorUtils.Error(
                            "アバターに VRC Avatar Descriptor が無いので、このコンポーネントは何もしない。" +
                            "先に VRChat SDK でアップロードできる状態にしてください。"));
                    } else {
                        warnings.Add(VRCFuryEditorUtils.Error(
                            "この SPS コンポーネントはアバターの中に置かれていないので、何もしない。" +
                            "アバターに含めるなら、シーンでアバターの横ではなく、アバターのオブジェクトの中に置いてください。"));
                    }
                }

                if (descriptors.Count > 1) {
                    warnings.Add(VRCFuryEditorUtils.Error(
                        "この階層に Avatar Descriptor が複数ある。Avatar Descriptor はアバターのルートに 1 つだけにしてください" +
                        "（このインスペクターやビルドで問題が起きることがある）。\n\n" + descriptors.Select(d => d.owner().GetDebugPath()).Join('\n')));
                }
            };

            ObjectExtensions.getExtraRecursiveTypes = original => {
                if (original is VRCExpressionsMenu) {
                    return new[] { typeof(VRCExpressionsMenu) };
                }
                return null;
            };
        }
    }
}
