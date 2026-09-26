using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using YozoLab.SPS.Actions;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Component;
using YozoLab.SPS.Exceptions;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.Feature;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;

namespace YozoLab.SPS.Ndmf {
    /**
     * SPS のビルド本体（VRCFury の VRCFuryBuilder に当たる）。NDMF のパスから呼ばれる。
     *
     * VRCFury との違い:
     * - 対象は SPS のコンポーネントだけ。VRCFury のコンポーネントがあっても何もしない。
     * - 動かすのは SPS に必要なサービスだけ（このパッケージに残したもの）。
     *   Write Defaults の統一、レイヤーの最適化、パラメータ圧縮のような、アバター全体を
     *   書き換えるサービスは持ち込んでいない。
     * - 出力は新しいコントローラー / メニュー / パラメータに作り、最後に SpsOutput が
     *   NDMF の仮想コントローラーと Modular Avatar のコンポーネントへ渡す。
     * - 生成物はディスクへ書かない（NDMF がビルドの最後に保存する）。
     */
    internal static class SpsBuilder {
        public static bool ShouldRun(VFGameObject avatarObject) {
            return avatarObject
                .GetComponentsInSelfAndChildren<SpsComponent>()
                .Any(c => !EditorOnlyUtils.IsInsideEditorOnly(c.owner()));
        }

        public static void Run(BuildContext buildContext) {
            var avatarObject = buildContext.AvatarRootObject.asVf();
            if (!ShouldRun(avatarObject)) return;

            var avatar = avatarObject.GetComponent<VRCAvatarDescriptor>();
            if (avatar == null) {
                throw new Exception("Failed to find VRCAvatarDescriptor on avatar object");
            }

            Debug.Log("YozoLab SPS invoked on " + avatarObject.name + " ...");
            using (new VRCFuryBuildContext()) {
                MaterialLocker.injectedAvatarObject = avatarObject;
                try {
                    Build(buildContext, avatarObject, avatar);
                } finally {
                    MaterialLocker.injectedAvatarObject = null;
                }
            }
            Debug.Log("YozoLab SPS Finished!");
        }

        private static void Build(BuildContext buildContext, VFGameObject avatarObject, VRCAvatarDescriptor avatar) {
            // 指などの当たり判定の位置を、ほかの処理が触る前に記録しておく
            OriginalContacts.Record(avatarObject);

            var ndmf = new NdmfSpsContext(buildContext);

            var currentServiceNumber = 0;
            var currentServiceGameObject = avatarObject;

            var actions = new List<FeatureBuilderAction>();
            var totalServiceCount = 0;
            var collectedModels = new List<FeatureModel>();
            var collectedBuilders = new List<FeatureBuilder>();

            var injector = new VRCFuryInjector();
            injector.ImportScan(typeof(VFServiceAttribute));
            injector.ImportScan(typeof(ActionBuilder));
            injector.Set(avatar);
            injector.Set("avatarObject", avatarObject);
            injector.Set(ndmf);
            var globals = new GlobalsService {
                avatarObject = avatarObject,
            };
            injector.Set(globals);
            injector.Set("componentObject", new Func<VFGameObject>(() => currentServiceGameObject));

            globals.addOtherFeature = (feature) => AddComponent(feature, currentServiceGameObject, currentServiceNumber);
            globals.allFeaturesInRun = collectedModels;
            globals.allBuildersInRun = collectedBuilders;

            // ビルドの最初の状態（パスとボーン）を控える
            injector.GetService<VRCFObjectPathCache>().Capture();
            injector.GetService<VRCFArmatureCache>().Capture();

            foreach (var service in injector.GetServices<object>()) {
                AddActionsFromObject(service, avatarObject);
            }

            void AddComponent(FeatureModel component, VFGameObject configObject, int? serviceNumOverride = null) {
                collectedModels.Add(component);

                FeatureBuilder builder;
                try {
                    builder = FeatureFinder.GetBuilder(component, configObject, injector);
                } catch (Exception e) {
                    throw new ExceptionWithCause(
                        $"Failed to load SPS component: {configObject.GetDebugPath()}",
                        e
                    );
                }

                if (builder == null) return;
                AddActionsFromObject(builder, configObject, serviceNumOverride);
            }

            void AddActionsFromObject(object service, VFGameObject configObject, int? serviceNumOverride = null) {
                var serviceNum = serviceNumOverride ?? ++totalServiceCount;
                if (service is FeatureBuilder builder) {
                    builder.uniqueModelNum = serviceNum;
                    builder.featureBaseObject = configObject;
                    collectedBuilders.Add(builder);
                }

                var actionMethods = service.GetType().GetMethods()
                    .Select(m => (m, m.GetCustomAttribute<FeatureBuilderActionAttribute>()))
                    .Where(tuple => tuple.Item2 != null)
                    .ToArray();
                foreach (var (method, attr) in actionMethods) {
                    actions.Add(new FeatureBuilderAction(attr, method, service, serviceNum, configObject));
                }
            }

            foreach (var c in avatarObject.GetComponentsInSelfAndChildren<SpsComponent>()) {
                c.Upgrade();
            }

            CheckExternalReferences(avatarObject);

            // SPS オプション（アバターに一つだけ）
            var optionComponents = avatarObject.GetComponentsInSelfAndChildren<SpsOptionsComponent>()
                .Where(c => !EditorOnlyUtils.IsInsideEditorOnly(c.owner()))
                .ToArray();
            if (optionComponents.Length > 1) {
                throw new Exception(
                    "This avatar contains multiple SPS Options components, but only one is allowed.\n\n"
                    + optionComponents.Select(c => c.owner().GetDebugPath()).JoinWithMore(10));
            }
            foreach (var options in optionComponents) {
                if (options.options != null) AddComponent(options.options, options.owner());
            }

            FeatureOrder? lastPriority = null;
            while (actions.Count > 0) {
                var action = actions.Min();
                actions.Remove(action);
                var service = action.GetService();
                if (action.configObject == null) continue;

                var priority = action.GetPriorty();
                if (lastPriority != priority) {
                    lastPriority = priority;
                    injector.GetService<RestingStateService>().OnPhaseChanged();
                }

                globals.currentMenuSortPosition = globals.currentFeatureNum = currentServiceNumber = action.serviceNum;
                var objectName = action.configObject.GetDebugPath();
                var currentModelName = $"{service.GetType().Name}.{action.GetName()} on {objectName}";
                globals.currentFeatureName = currentModelName;
                globals.currentFeatureClipPrefix = $"VF{currentServiceNumber} {(service as FeatureBuilder)?.GetClipPrefix() ?? service.GetType().Name}";
                currentServiceGameObject = action.configObject;
                globals.currentFeatureObjectPath = action.configObject.GetPath(avatarObject);

                try {
                    action.Call();
                } catch (Exception e) {
                    throw new ExceptionWithCause($"Failed to build SPS component: {currentModelName}", VRCFExceptionUtils.GetGoodCause(e));
                }
            }

            SpsOutput.Apply(injector, ndmf, avatarObject);
        }

        /**
         * SPS のコンポーネントがアバターの外を参照していないか。
         * コンポーネントを別のアバターからコピーしたときによく起きる。
         */
        private static void CheckExternalReferences(VFGameObject avatarObject) {
            var externalReferences = new List<string>();
            foreach (var component in avatarObject.GetComponentsInSelfAndChildren<SpsComponent>()) {
                MutableManager.ForEachChildObjectReference(component, (path, target, set) => {
                    var targetObject = target switch {
                        GameObject gameObject => gameObject.asVf(),
                        UnityEngine.Component targetComponent => targetComponent.owner(),
                        _ => null
                    };
                    if (targetObject == null) return;
                    if (targetObject.IsSameOrChildOf(avatarObject)) return;

                    var targetPath = EditorUtility.IsPersistent(target)
                        ? target.GetPathAndName()
                        : targetObject.GetDebugPath();
                    externalReferences.Add($"{component.owner().GetDebugPath()} -> {targetPath}");
                });
            }
            if (externalReferences.Count > 0) {
                throw new Exception(
                    "One of your SPS components is referencing an object outside of the avatar. This is a mistake, and can " +
                    "happen if you incorrectly copied individual components between avatars. You need to fix these references, or just " +
                    "delete the SPS component since it probably doesn't work anyways.\n\n"
                    + externalReferences.JoinWithMore(10)
                );
            }
        }
    }
}
