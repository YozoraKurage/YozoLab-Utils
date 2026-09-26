using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using YozoLab.SPS.Hooks.UnityFixes;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Service;
using YozoLab.SPS.Utils;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Ndmf {
    /**
     * SPS の出力をアバターへ渡す。
     *
     * - アニメーター: SPS のコントローラーを NDMF の仮想コントローラーに変換し、アバターの
     *   同じ種類（FX など）のコントローラーの末尾へレイヤーを足す。既存のレイヤーは動かさない。
     *   Write Defaults は Modular Avatar の Merge Animator と同じ規則でアバターに合わせる
     *   （ダイレクトブレンドツリー 1 つだけのレイヤーは常に ON）。アバターが WD OFF のときは、
     *   SPS が動かす値の初期値を書く「SPS Defaults」レイヤーを基底レイヤーのすぐ下に置く。
     * - パラメータ: Modular Avatar Parameters として宣言し、MA に Expression Parameters へ入れさせる。
     * - メニュー: Modular Avatar Menu Installer で設置させる（8 項目を超えたときのページ送りも MA）。
     *
     * MA のコンポーネントはアバター直下の「YozoLab SPS」オブジェクトに付ける。
     * MA がビルドの中で処理し、最後に取り除く。
     */
    internal static class SpsOutput {
        private const string HolderName = "YozoLab SPS";

        public static void Apply(VRCFuryInjector injector, NdmfSpsContext ndmf, VFGameObject avatarObject) {
            injector.GetService<ExistingClipsService>().Commit();

            var controllers = injector.GetService<ControllersService>();
            foreach (var controller in controllers.GetAllMutatedControllers()) {
                MergeController(ndmf, avatarObject, controller);
            }

            GameObject holder = null;
            GameObject GetHolder() {
                if (holder != null) return holder;
                holder = new GameObject(HolderName);
                holder.transform.SetParent((Transform)avatarObject, false);
                return holder;
            }

            var syncedParams = injector.GetService<ParamsService>().GetParams().GetRaw();
            if (syncedParams.parameters.Length > 0) {
                EmitParameters(GetHolder(), syncedParams);
            }

            var menuService = injector.GetService<MenuService>();
            if (menuService.HasMenu) {
                var menu = menuService.GetMenu();
                FinalizeMenu(menu);
                EmitMenu(GetHolder(), menu.GetRaw(), menuService.GetReadOnlyMenu());
            }

            Unity6RendererFixHook.Process();
        }

        // ---------------------------------------------------------------
        // アニメーター
        // ---------------------------------------------------------------

        private static void MergeController(NdmfSpsContext ndmf, VFGameObject avatarObject, ControllerManager controller) {
            var type = controller.GetType();
            var saved = controller.Save(avatarObject, SaveAssetsSession.InMemory());
            var source = ndmf.Controllers.Clone(saved);
            if (source == null) return;

            var ourLayers = source.Layers.Where(l => !IsEmpty(l)).ToList();
            if (ourLayers.Count == 0 && source.Parameters.Count == 0) return;

            if (!ndmf.Controllers.Controllers.TryGetValue(type, out var target) || target == null) {
                // アバターにこの種類のコントローラーが無い。SPS のものをそのまま使う。
                VirtualizePaths(ndmf, source);
                ndmf.Controllers.Controllers[type] = source;
                return;
            }

            var avatarWriteDefaults = GetUniformWriteDefaults(target);

            // WD OFF のアバターでは、SPS が動かす値の初期値を別レイヤーで書いておく
            // （パスを追跡用に直す前に、実際のパスで今の値を読む必要がある）
            VirtualLayer defaultsLayer = null;
            if (avatarWriteDefaults == false) {
                defaultsLayer = MakeDefaultsLayer(ndmf, avatarObject, source, ourLayers);
            }

            VirtualizePaths(ndmf, source);
            MergeParameters(target, source);

            foreach (var layer in ourLayers) {
                if (IsWriteDefaultsRequired(layer)) {
                    SetWriteDefaults(layer, true);
                } else if (avatarWriteDefaults != null) {
                    SetWriteDefaults(layer, avatarWriteDefaults.Value);
                }
            }

            if (defaultsLayer != null) {
                var layers = target.Layers.ToList();
                layers.Insert(Math.Min(1, layers.Count), defaultsLayer);
                target.Layers = layers;
            }

            foreach (var layer in ourLayers) {
                target.AddLayer(LayerPriority.Default, layer);
            }
        }

        private static bool IsEmpty(VirtualLayer layer) {
            var sm = layer.StateMachine;
            if (sm == null) return true;
            return !sm.AllStates().Any();
        }

        private static void VirtualizePaths(NdmfSpsContext ndmf, VirtualAnimatorController controller) {
            foreach (var clip in controller.AllReachableNodes().OfType<VirtualClip>()) {
                clip.EditPaths(ndmf.VirtualizePath);
            }
        }

        private static void MergeParameters(VirtualAnimatorController target, VirtualAnimatorController source) {
            // Modular Avatar の Merge Animator と同じく、型が食い違ったら float にそろえる
            var destParams = target.Parameters;
            foreach (var (name, parameter) in source.Parameters) {
                if (destParams.TryGetValue(name, out var existing)) {
                    if (existing.type != parameter.type) {
                        destParams = destParams.SetItem(name, new AnimatorControllerParameter {
                            type = AnimatorControllerParameterType.Float,
                            name = name,
                            defaultFloat = existing.type switch {
                                AnimatorControllerParameterType.Bool => existing.defaultBool ? 1 : 0,
                                AnimatorControllerParameterType.Int => existing.defaultInt,
                                _ => existing.defaultFloat
                            }
                        });
                    }
                    continue;
                }
                destParams = destParams.SetItem(name, parameter);
            }
            target.Parameters = destParams;
        }

        /**
         * アバターの既存レイヤーの Write Defaults がそろっていれば、その値。ばらばらなら null。
         * Modular Avatar の Merge Animator（Match Avatar Write Defaults）と同じ判定。
         */
        internal static bool? GetUniformWriteDefaults(VirtualAnimatorController controller) {
            var values = controller.Layers
                .Where(l => l.StateMachine != null)
                .Where(l => !IsWriteDefaultsRequired(l))
                .SelectMany(l => l.StateMachine.AllStates())
                .Select(s => s.WriteDefaultValues)
                .Distinct()
                .ToList();
            return values.Count == 1 ? values[0] : (bool?)null;
        }

        /** ダイレクトブレンドツリー 1 つだけのレイヤー（と加算レイヤー）は WD ON でないと正しく動かない。 */
        internal static bool IsWriteDefaultsRequired(VirtualLayer layer) {
            if (layer.BlendingMode == AnimatorLayerBlendingMode.Additive) return true;
            var sm = layer.StateMachine;
            if (sm == null) return false;
            if (sm.StateMachines.Count != 0) return false;
            if (sm.States.Count != 1) return false;
            if (sm.AnyStateTransitions.Count != 0) return false;
            if (sm.DefaultState?.Transitions?.Count != 0) return false;
            if (!(sm.DefaultState.Motion is VirtualBlendTree)) return false;
            return sm.DefaultState.Motion.AllReachableNodes()
                .OfType<VirtualBlendTree>()
                .Any(bt => bt.BlendType == BlendTreeType.Direct);
        }

        private static void SetWriteDefaults(VirtualLayer layer, bool value) {
            foreach (var state in layer.StateMachine?.AllStates() ?? Array.Empty<VirtualState>()) {
                state.WriteDefaultValues = value;
            }
        }

        /**
         * WD OFF のアバター向けに、SPS のレイヤー（WD を合わせるもの）が動かす値の初期値を書くレイヤー。
         * 基底レイヤーのすぐ下に置くので、アバターの既存レイヤーが同じ値を動かしていても邪魔しない。
         * 初期値は今のアバターの状態（SPS が休止状態を反映した後）から読む。
         */
        private static VirtualLayer MakeDefaultsLayer(
            NdmfSpsContext ndmf,
            VFGameObject avatarObject,
            VirtualAnimatorController source,
            IEnumerable<VirtualLayer> ourLayers
        ) {
            GameObject root = avatarObject;
            var clips = ourLayers
                .Where(l => !IsWriteDefaultsRequired(l))
                .SelectMany(l => l.AllReachableNodes())
                .OfType<VirtualClip>()
                .Distinct()
                .ToList();
            if (clips.Count == 0) return null;

            var context = ndmf.Controllers.CloneContext;
            var defaultsClip = VirtualClip.Create("SPS Defaults");
            var floatBindings = new HashSet<EditorCurveBinding>(clips.SelectMany(c => c.GetFloatCurveBindings()));
            var objectBindings = new HashSet<EditorCurveBinding>(clips.SelectMany(c => c.GetObjectCurveBindings()));

            foreach (var binding in floatBindings) {
                float value;
                if (binding.type == typeof(Animator) && binding.path == "") {
                    if (!source.Parameters.TryGetValue(binding.propertyName, out var p)) continue;
                    value = p.type switch {
                        AnimatorControllerParameterType.Bool => p.defaultBool ? 1 : 0,
                        AnimatorControllerParameterType.Int => p.defaultInt,
                        _ => p.defaultFloat
                    };
                } else if (!AnimationUtility.GetFloatValue(root, binding, out value)) {
                    continue;
                }
                defaultsClip.SetFloatCurve(binding, AnimationCurve.Constant(0, 0, value));
            }
            foreach (var binding in objectBindings) {
                if (!AnimationUtility.GetObjectReferenceValue(root, binding, out var value)) continue;
                defaultsClip.SetObjectCurve(binding, new[] { new ObjectReferenceKeyframe { time = 0, value = value } });
            }
            defaultsClip.EditPaths(ndmf.VirtualizePath);

            var layer = VirtualLayer.Create(context, "SPS Defaults");
            var stateMachine = VirtualStateMachine.Create(context, "SPS Defaults");
            layer.StateMachine = stateMachine;
            var state = stateMachine.AddState("Defaults", defaultsClip);
            state.WriteDefaultValues = false;
            stateMachine.DefaultState = state;
            return layer;
        }

        // ---------------------------------------------------------------
        // パラメータ
        // ---------------------------------------------------------------

        private static void EmitParameters(GameObject holder, VRCExpressionParameters syncedParams) {
            var component = holder.AddComponent<ModularAvatarParameters>();
            foreach (var p in syncedParams.parameters) {
                if (p == null || string.IsNullOrEmpty(p.name)) continue;
                component.parameters.Add(new ParameterConfig {
                    nameOrPrefix = p.name,
                    remapTo = "",
                    internalParameter = false,
                    isPrefix = false,
                    syncType = p.valueType switch {
                        VRCExpressionParameters.ValueType.Int => ParameterSyncType.Int,
                        VRCExpressionParameters.ValueType.Float => ParameterSyncType.Float,
                        _ => ParameterSyncType.Bool
                    },
                    localOnly = !p.networkSynced,
                    defaultValue = p.defaultValue,
                    saved = p.saved,
                    hasExplicitDefaultValue = true,
                });
            }
        }

        // ---------------------------------------------------------------
        // メニュー
        // ---------------------------------------------------------------

        /** VRCFury の FinalizeMenuService のうち、SPS のメニューに要る部分（並べ替えと項目の整形）。 */
        private static void FinalizeMenu(MenuManager menu) {
            menu.SortMenu();
            menu.GetRaw().ForEachMenu(ForEachItem: (control, path) => {
                if (!VRCFEnumUtils.IsValid(control.type)) {
                    control.type = VRCExpressionsMenu.Control.ControlType.Button;
                }
                if (control.parameter == null) {
                    control.parameter = new VRCExpressionsMenu.Control.Parameter { name = "" };
                }
                if (control.subParameters == null) {
                    control.subParameters = new VRCExpressionsMenu.Control.Parameter[] { };
                }
                if (control.type != VRCExpressionsMenu.Control.ControlType.SubMenu) {
                    control.subMenu = null;
                }
                return VRCExpressionsMenuExtensions.ForEachMenuItemResult.Continue;
            });
        }

        /**
         * SPS のメニューを設置する。SPS オプションのメニューパスの途中までがアバターの既存メニューに
         * あれば、その既存のサブメニューへ入れる（同じ名前のサブメニューを二重に作らないため。
         * VRCFury も既存のサブメニューへ入れていた）。
         */
        private static void EmitMenu(GameObject holder, VRCExpressionsMenu ourMenu, VRCExpressionsMenu avatarMenu) {
            Install(holder, ourMenu.controls, avatarMenu, isRoot: true);
        }

        private static void Install(
            GameObject holder,
            IEnumerable<VRCExpressionsMenu.Control> controls,
            VRCExpressionsMenu existing,
            bool isRoot
        ) {
            var toInstall = new List<VRCExpressionsMenu.Control>();
            foreach (var control in controls) {
                var existingSubmenu = FindSubmenu(existing, control);
                if (existingSubmenu != null && control.subMenu != null) {
                    Install(holder, control.subMenu.controls, existingSubmenu, isRoot: false);
                } else {
                    toInstall.Add(control);
                }
            }
            if (toInstall.Count == 0) return;

            var menuToAppend = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menuToAppend.name = "SPS";
            menuToAppend.controls = toInstall;

            var obj = new GameObject("Menu");
            obj.transform.SetParent(holder.transform, false);
            var installer = obj.AddComponent<ModularAvatarMenuInstaller>();
            installer.menuToAppend = menuToAppend;
            // null ならアバターのルートメニューに入る
            installer.installTargetMenu = isRoot ? null : existing;
        }

        private static VRCExpressionsMenu FindSubmenu(VRCExpressionsMenu existing, VRCExpressionsMenu.Control control) {
            if (existing == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu) return null;
            return existing.controls
                .Where(c => c != null
                            && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                            && c.subMenu != null
                            && c.name == control.name)
                .Select(c => c.subMenu)
                .FirstOrDefault();
        }
    }
}
