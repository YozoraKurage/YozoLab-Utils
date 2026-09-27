using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Exceptions;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Menu;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Inspector {
    [CustomEditor(typeof(SpsPlug), true)]
    internal class SpsPlugEditor : VRCFuryComponentEditor<SpsPlug> {
        internal const int SpsTagRuleCount = 2;
        public const string SpsParamDepth = "Depth";
        public const string SpsParamVelocity = "Velocity";
        public const string SpsParamPlugLength = "Plug Length";
        public const string SpsParamPlugRadius = "Plug Radius";
        public const string SpsUnitsMeters = "Meters";
        public const string SpsUnitsLocal = "Local";
        public const string SpsUnitsPlugLengths = "Plug Lengths";
        public const string SpsDepthMeters = "__sps_depth_meters";
        public const string SpsDepthLocal = "__sps_depth_local";
        public const string SpsDepthPlugLengths = "__sps_depth_plug_lengths";
        public const string SpsVelocityMeters = "__sps_velocity_meters";
        public const string SpsVelocityLocal = "__sps_velocity_local";
        public const string SpsVelocityPlugLengths = "__sps_velocity_plug_lengths";
        public const string SpsPlugLengthMeters = "__sps_plug_length_meters";
        public const string SpsPlugLengthLocal = "__sps_plug_length_local";
        public const string SpsPlugLengthPlugLengths = "__sps_plug_length_plug_lengths";
        public const string SpsPlugRadiusMeters = "__sps_plug_radius_meters";
        public const string SpsPlugRadiusLocal = "__sps_plug_radius_local";
        public const string SpsPlugRadiusPlugLengths = "__sps_plug_radius_plug_lengths";

        public static readonly string[] SpsParams = {
            SpsParamDepth,
            SpsParamVelocity,
            SpsParamPlugLength,
            SpsParamPlugRadius
        };
        public static readonly string[] SpsUnits = {
            SpsUnitsMeters,
            SpsUnitsLocal,
            SpsUnitsPlugLengths
        };

        public static string GetSpsMagicParam(string param, string units) {
            if (param == SpsParamDepth && units == SpsUnitsMeters) return SpsDepthMeters;
            if (param == SpsParamDepth && units == SpsUnitsLocal) return SpsDepthLocal;
            if (param == SpsParamDepth && units == SpsUnitsPlugLengths) return SpsDepthPlugLengths;
            if (param == SpsParamVelocity && units == SpsUnitsMeters) return SpsVelocityMeters;
            if (param == SpsParamVelocity && units == SpsUnitsLocal) return SpsVelocityLocal;
            if (param == SpsParamVelocity && units == SpsUnitsPlugLengths) return SpsVelocityPlugLengths;
            if (param == SpsParamPlugLength && units == SpsUnitsMeters) return SpsPlugLengthMeters;
            if (param == SpsParamPlugLength && units == SpsUnitsLocal) return SpsPlugLengthLocal;
            if (param == SpsParamPlugLength && units == SpsUnitsPlugLengths) return SpsPlugLengthPlugLengths;
            if (param == SpsParamPlugRadius && units == SpsUnitsMeters) return SpsPlugRadiusMeters;
            if (param == SpsParamPlugRadius && units == SpsUnitsLocal) return SpsPlugRadiusLocal;
            if (param == SpsParamPlugRadius && units == SpsUnitsPlugLengths) return SpsPlugRadiusPlugLengths;
            return "";
        }

        public static (string param, string units)? ParseSpsMagicParam(string sourceParam) {
            switch (sourceParam) {
                case SpsDepthMeters: return (SpsParamDepth, SpsUnitsMeters);
                case SpsDepthLocal: return (SpsParamDepth, SpsUnitsLocal);
                case SpsDepthPlugLengths: return (SpsParamDepth, SpsUnitsPlugLengths);
                case SpsVelocityMeters: return (SpsParamVelocity, SpsUnitsMeters);
                case SpsVelocityLocal: return (SpsParamVelocity, SpsUnitsLocal);
                case SpsVelocityPlugLengths: return (SpsParamVelocity, SpsUnitsPlugLengths);
                case SpsPlugLengthMeters: return (SpsParamPlugLength, SpsUnitsMeters);
                case SpsPlugLengthLocal: return (SpsParamPlugLength, SpsUnitsLocal);
                case SpsPlugLengthPlugLengths: return (SpsParamPlugLength, SpsUnitsPlugLengths);
                case SpsPlugRadiusMeters: return (SpsParamPlugRadius, SpsUnitsMeters);
                case SpsPlugRadiusLocal: return (SpsParamPlugRadius, SpsUnitsLocal);
                case SpsPlugRadiusPlugLengths: return (SpsParamPlugRadius, SpsUnitsPlugLengths);
                default: return null;
            }
        }

        private static string FormatSpsParam(string param) {
            switch (param) {
                case SpsParamDepth: return "深さ";
                case SpsParamVelocity: return "速さ";
                case SpsParamPlugLength: return "Plug の長さ";
                case SpsParamPlugRadius: return "Plug の半径";
                default: return param;
            }
        }

        private static string FormatSpsUnits(string units) {
            switch (units) {
                case SpsUnitsMeters: return "メートル";
                case SpsUnitsLocal: return "ローカル単位";
                case SpsUnitsPlugLengths: return "Plug の長さ比";
                default: return units;
            }
        }

        public static VisualElement RenderSpsInjectParamEditor(SerializedProperty injectProp) {
            var sourceParamProp = injectProp.FindPropertyRelative("sourceParam");
            var parsed = ParseSpsMagicParam(sourceParamProp.stringValue);
            var selectedParam = parsed?.param ?? SpsParamDepth;
            var selectedUnits = parsed?.units ?? SpsUnitsMeters;

            var content = new VisualElement();
            var paramField = new PopupField<string>("値", SpsParams.ToList(), selectedParam, FormatSpsParam, FormatSpsParam);
            var unitsField = new PopupField<string>("単位", SpsUnits.ToList(), selectedUnits, FormatSpsUnits, FormatSpsUnits);

            void Save() {
                sourceParamProp.stringValue = GetSpsMagicParam(paramField.value, unitsField.value);
                sourceParamProp.serializedObject.ApplyModifiedProperties();
            }

            paramField.RegisterValueChangedCallback(_ => Save());
            unitsField.RegisterValueChangedCallback(_ => Save());

            content.Add(paramField);
            content.Add(unitsField);
            return content;
        }

        public static VisualElement SpsTagProp(SerializedProperty prop, string label) {
            return VRCFuryEditorUtils.Prop(prop, label, fieldOverride: SpsTagTextField(prop));
        }

        /** タグ名の入力欄。英小文字と数字だけにそろえる。 */
        public static TextField SpsTagTextField(SerializedProperty prop) {
            var field = new TextField {
                isDelayed = true,
                tooltip = "英小文字と数字だけ（それ以外は取り除かれる）"
            };
            field.SetValueWithoutNotify(SanitizeSpsTag(prop.stringValue));
            field.RegisterValueChangedCallback(cb => {
                var sanitized = SanitizeSpsTag(cb.newValue);
                if (field.value != sanitized) {
                    field.SetValueWithoutNotify(sanitized);
                }
                prop.stringValue = sanitized;
                prop.serializedObject.ApplyModifiedProperties();
            });
            return field;
        }

        public static string SanitizeSpsTag(string input) {
            if (string.IsNullOrEmpty(input)) return "";

            var chars = input
                .Trim()
                .ToLowerInvariant()
                .Where(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
                .ToArray();
            return new string(chars);
        }

        private static SerializedProperty AddSpsTagRule(SerializedProperty listProp) {
            if (listProp.arraySize >= SpsTagRuleCount) return null;
            var index = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(index);
            var item = listProp.GetArrayElementAtIndex(index);
            item.FindPropertyRelative("tag").stringValue = "";
            item.FindPropertyRelative("allowSelf").boolValue = true;
            item.FindPropertyRelative("allowOthers").boolValue = true;
            listProp.serializedObject.ApplyModifiedProperties();
            return item;
        }

        /** タグ 1 つ分の行：[タグ名] [自分] [他の人] [×] */
        private static VisualElement SpsTagRuleProp(SerializedProperty listProp, int index) {
            var prop = listProp.GetArrayElementAtIndex(index);
            var row = new VisualElement().Row().AlignItems(Align.Center);
            row.AddToClassList("spsProp");

            var tagField = SpsTagTextField(prop.FindPropertyRelative("tag"));
            tagField.style.flexGrow = 1;
            tagField.style.flexShrink = 1;
            tagField.style.minWidth = 60;
            row.Add(tagField);

            Toggle SmallToggle(string relative, string text, string tooltip) {
                var toggle = new Toggle(text) { bindingPath = prop.FindPropertyRelative(relative).propertyPath, tooltip = tooltip };
                toggle.style.marginLeft = 8;
                toggle.labelElement.style.minWidth = 0;
                toggle.labelElement.style.marginRight = 2;
                return toggle;
            }
            row.Add(SmallToggle("allowSelf", "自分", "自分のアバターの Socket に当てはめる"));
            row.Add(SmallToggle("allowOthers", "他の人", "他の人のアバターの Socket に当てはめる"));

            var remove = new Button(() => {
                listProp.DeleteArrayElementAtIndex(index);
                listProp.serializedObject.ApplyModifiedProperties();
            }) {
                text = "×",
                tooltip = "このタグを消す"
            };
            remove.style.marginLeft = 6;
            row.Add(remove);
            return row;
        }

        private static VisualElement SpsTagRuleList(SerializedProperty listProp, string label, string tooltip) {
            return VRCFuryEditorUtils.RefreshOnChange(() => {
                var container = new VisualElement();
                var (labelBox, _) = VRCFuryEditorUtils.CreateTooltip(label, tooltip);
                labelBox.AddToClassList("spsLabelOwnLine");
                container.Add(labelBox);
                for (var i = 0; i < Math.Min(listProp.arraySize, SpsTagRuleCount); i++) {
                    container.Add(SpsTagRuleProp(listProp, i));
                }
                if (listProp.arraySize < SpsTagRuleCount) {
                    var add = new Button(() => AddSpsTagRule(listProp)) { text = "＋ タグを追加" };
                    add.style.alignSelf = Align.FlexStart;
                    add.AddToClassList("spsProp");
                    container.Add(add);
                }
                return container;
            }, listProp);
        }

        internal static string GetOscId(SpsPlug plug) {
            return HapticUtils.GetPreferredId(
                plug,
                p => p.name,
                p => {
                    var renderers = GetRenderers(p);
                    if (renderers.Count > 0) {
                        return HapticUtils.GetFallbackId(renderers.First().owner());
                    }

                    return HapticUtils.GetFallbackId(p.owner());
                }
            );
        }

        protected override VisualElement CreateEditor(SerializedObject serializedObject, SpsPlug target) {
            var container = new VisualElement();
            var configureTps = serializedObject.FindProperty("configureTps");
            var enableSps = serializedObject.FindProperty("enableSps");

            // 注意書きはまとめて一番上に出す
            var notices = new VisualElement();
            notices.AddToClassList("spsNotices");
            container.Add(notices);
            if (DexProtectUtils.IsDexProtectPresent()) {
                notices.Add(VRCFuryEditorUtils.Warn("このアバターは DexProtect を使っている。変形のときに Plug の大きさが正しくならないことがある。気になるなら DexProtect を外してください。"));
            }
            if (MaterialLocker.UsesD4rk(target.owner().uploadRoots.First(), false)) {
                notices.Add(VRCFuryEditorUtils.Warn("このアバターは d4rk Avatar Optimizer を使っている。変形が崩れることがある。気になるなら d4rk Avatar Optimizer を外してください。"));
            }
            notices.Add(ConstraintWarning(target));
            var boneWarning = VRCFuryEditorUtils.Warn(
                "メッシュにボーンが入っているのに、SPS Plug がボーンの中に置かれていない。" +
                "ボーン入りのメッシュでは、根元にいちばん近いボーンの子に SPS Plug を置いてください。");
            boneWarning.SetVisible(false);
            notices.Add(boneWarning);

            // ---- メッシュとサイズ ----
            var sizeSection = VRCFuryEditorUtils.Section("メッシュとサイズ");
            container.Add(sizeSection);

            var autoMesh = serializedObject.FindProperty("autoRenderer");
            sizeSection.Add(VRCFuryEditorUtils.BetterProp(autoMesh, "メッシュを自動で探す",
                tooltip: "この Plug の近く（同じオブジェクト・親・ボーンのウェイト）からメッシュを探す"));
            sizeSection.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                if (autoMesh.boolValue) return new VisualElement();
                return VRCFuryEditorUtils.BetterProp(
                    serializedObject.FindProperty("configureTpsMesh"),
                    "対象のメッシュ",
                    fieldOverride: VRCFuryEditorUtils.List(serializedObject.FindProperty("configureTpsMesh")));
            }, autoMesh));

            var autoLength = serializedObject.FindProperty("autoLength");
            sizeSection.Add(VRCFuryEditorUtils.BetterProp(autoLength, "長さをメッシュから測る"));
            sizeSection.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                if (autoLength.boolValue) return new VisualElement();
                return VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("length"), "長さ",
                    tooltip: "メートル（古い設定の「ワールド単位」を切っているときはローカル単位）");
            }, autoLength));

            var autoRadius = serializedObject.FindProperty("autoRadius");
            sizeSection.Add(VRCFuryEditorUtils.BetterProp(autoRadius, "太さをメッシュから測る"));
            sizeSection.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                if (autoRadius.boolValue) return new VisualElement();
                return VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("radius"), "半径",
                    tooltip: "メートル（古い設定の「ワールド単位」を切っているときはローカル単位）");
            }, autoRadius));

            sizeSection.Add(VRCFuryEditorUtils.BetterProp(
                serializedObject.FindProperty("useBoneMask"),
                "ボーンのウェイトで範囲を決める",
                tooltip: "Plug のボーンにウェイトが乗っている頂点だけを、変形と長さの計算に使う"
            ));
            sizeSection.Add(VRCFuryEditorUtils.BetterProp(
                serializedObject.FindProperty("textureMask"),
                "マスク用テクスチャ",
                tooltip: "任意。白い部分は変形させず、長さの計算にも使わない"
            ));

            sizeSection.Add(VRCFuryEditorUtils.Debug(refreshMessage: () => {
                var size = PlugSizeDetector.GetWorldSize(target);
                var text = new List<string>();
                text.Add("メッシュ: " + size.renderers.Select(r => r.owner().name).Join(", "));
                text.Add($"長さ {size.worldLength:0.000} m ／ 半径 {size.worldRadius:0.000} m");

                var slots = new List<string>();
                foreach (var renderer in size.matSlots.GetKeys()) {
                    foreach (var slot in size.matSlots.Get(renderer)) {
                        var matName = renderer.GetComponent<Renderer>()?.sharedMaterials[slot]?.name ?? "未設定";
                        slots.Add($"{renderer.name} #{slot}（{matName}）");
                    }
                }
                if (slots.Count > 0) text.Add("加工するマテリアル: " + slots.Join(", "));

                var bones = size.renderers.OfType<SkinnedMeshRenderer>()
                    .SelectMany(skin => skin.bones)
                    .Where(bone => bone != null)
                    .ToArray();
                var isInsideBone = bones.Any(bone => target.owner().IsSameOrChildOf(bone));
                boneWarning.SetVisible(bones.Length > 0 && !isInsideBone);

                return text.Join('\n');
            }));

            // ---- 変形 ----
            var spsSection = VRCFuryEditorUtils.Section("変形（SPS）", "近くの Socket に向かって曲がる（SPS / TPS / DPS の Socket が対象）");
            container.Add(spsSection);
            spsSection.Add(VRCFuryEditorUtils.BetterProp(enableSps, "変形させる"));
            spsSection.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                var c = new VisualElement();
                if (!enableSps.boolValue) return c;

                c.Add(VRCFuryEditorUtils.BetterProp(
                    serializedObject.FindProperty("spsAutorig"),
                    "ボーンを自動で入れる",
                    tooltip: "メッシュにボーンが無いとき、ボーンと PhysBone を足して揺れるようにする"
                ));

                var animatedProp = serializedObject.FindProperty("spsAnimatedEnabled");
                var animatedField = new Toggle();
                animatedField.SetValueWithoutNotify(animatedProp.floatValue > 0);
                animatedField.RegisterValueChangedCallback(cb => {
                    animatedProp.floatValue = cb.newValue ? 1 : 0;
                    animatedProp.serializedObject.ApplyModifiedProperties();
                });
                c.Add(VRCFuryEditorUtils.BetterProp(
                    animatedProp,
                    "変形 ON（アニメーション用）",
                    fieldOverride: animatedField,
                    tooltip: "アニメーションでこの値を切り替えると、場面に応じて変形を止められる"
                ));
                c.Add(VRCFuryEditorUtils.BetterProp(
                    serializedObject.FindProperty("spsOverrun"),
                    "穴の少し奥まで伸ばす",
                    tooltip: "穴の入口をわずかに越えて伸ばし、潰れ方を自然にする。" +
                             "切ると、穴の手前で地図を畳んだように折れて見えることがある"
                ));
                c.Add(VRCFuryEditorUtils.BetterProp(
                    serializedObject.FindProperty("spsBlendshapes"),
                    "変形中も動かすブレンドシェイプ",
                    fieldOverride: VRCFuryEditorUtils.List(serializedObject.FindProperty("spsBlendshapes")),
                    tooltip: "変形中のブレンドシェイプは、ふつうエディタでの見た目に固定される。" +
                             "ここに挙げたもの（16 個まで）は変形中もアニメーションで動かせる"
                ));
                c.Add(VRCFuryEditorUtils.BetterProp(
                    serializedObject.FindProperty("postBakeActions"),
                    "計算の後に適用するアクション",
                    tooltip: "長さや向きの計算のため、Plug はまっすぐな姿勢で置いておく必要がある。" +
                             "ゲーム内の普段の見た目を変えたいときは、ここにアクションを足す（計算の後にアバターへ適用される）"
                ));
                return c;
            }, enableSps));

            // ---- 深度アニメーション ----
            var depthList = serializedObject.FindProperty("depthActions2");
            var depth = VRCFuryEditorUtils.Group("深度アニメーション", "plug.depth",
                defaultOpen: depthList.arraySize > 0,
                summary: () => depthList.arraySize > 0 ? $"{depthList.arraySize} 件" : "なし");
            depth.Add(VRCFuryEditorUtils.WrappedLabel("Socket との距離に応じて、好きなものを動かす").AddClass("spsSubtitle"));
            depth.Add(VRCFuryEditorUtils.List(depthList, () => {
                VRCFuryEditorUtils.AddToList(depthList, newAction => {
                    newAction.FindPropertyRelative("range").vector2Value = new Vector2(-1, 0);
                    newAction.FindPropertyRelative("units").enumValueIndex =
                        (int)SpsSocket.DepthActionUnits.Plugs;
                });
            }));
            container.Add(depth);

            // ---- ターゲットの絞り込み ----
            var useSharedTag = serializedObject.FindProperty("useSharedTag");
            var includeTags = serializedObject.FindProperty("includeTags");
            var excludeTags = serializedObject.FindProperty("excludeTags");
            var tags = VRCFuryEditorUtils.Group("狙う Socket の絞り込み", "plug.tags",
                summary: () => !useSharedTag.boolValue ? "共通タグなし" :
                    includeTags.arraySize + excludeTags.arraySize > 0 ? "タグあり" : "既定");
            tags.Add(VRCFuryEditorUtils.BetterProp(useSharedTag, "共通の SPS タグを含める",
                tooltip: "ふつうの Socket はこのタグで狙える。切ると、ほとんどの Socket を狙わなくなる"));
            tags.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                if (useSharedTag.boolValue) return new VisualElement();
                return VRCFuryEditorUtils.Warn("共通の SPS タグを含めていないので、ほとんどの Socket を狙わない。");
            }, useSharedTag));
            tags.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("useLights"), "古い形式の Socket も狙う",
                tooltip: "ライトで位置を示す SPS1 / DPS / TPS の Socket も狙う"));
            var useHipAvoidance = serializedObject.FindProperty("useHipAvoidance");
            tags.Add(VRCFuryEditorUtils.BetterProp(
                useHipAvoidance,
                "自分の腰の Socket は狙わない",
                tooltip: "この Plug が腰にあるとき、同じく腰にある自分の Socket を狙わない"
            ));
            tags.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                if (!useHipAvoidance.boolValue) return new VisualElement();
                var autoTagGenerator = VRCFuryPerFrameInjector.GetPerFrameInjector(target.owner())
                    .GetServices<SpsAutoTagGenerator>()
                    .FirstOrDefault();
                if (autoTagGenerator?.GetClosestBone(target.owner()) != HumanBodyBones.Hips) {
                    return new VisualElement();
                }
                return VRCFuryEditorUtils.Info("この Plug は腰にあるので、自分の腰の Socket は狙わない。");
            }, useHipAvoidance));
            tags.Add(SpsTagRuleList(includeTags, "狙うタグ", "このタグを持つ Socket も狙う"));
            tags.Add(SpsTagRuleList(excludeTags, "狙わないタグ", "このタグを持つ Socket は狙わない"));
            container.Add(tags);

            // ---- 触覚 ----
            container.Add(GetOgbHapticsSection("plug.haptics", haptics => {
                haptics.Add(SpsEditorUtils.AutoHapticIdProp(
                    serializedObject.FindProperty("name"),
                    "OGB に送る名前",
                    target,
                    target.owner(),
                    avatar => avatar.GetComponentsInSelfAndChildren<SpsPlug>(),
                    GetOscId
                ));
            }));

            // ---- 古い設定 ----
            var legacy = VRCFuryEditorUtils.Group("古い設定（非推奨）", "plug.legacy");
            legacy.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                if (configureTps.boolValue || enableSps.boolValue) return new VisualElement();
                return VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("autoPosition"),
                    "位置と向きをメッシュから決める");
            }, configureTps, enableSps));
            legacy.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("unitsInMeters"), "長さ・半径をワールド単位（メートル）で扱う"));
            legacy.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("useLegacyRendererFinder"), "古いメッシュの探し方を使う"));
            legacy.Add(VRCFuryEditorUtils.BetterProp(configureTps, "Poiyomi の TPS を自動で設定する"));
            legacy.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("addDpsTipLight"), "DPS 用の先端ライトを足す",
                tooltip: "メニューで有効にしたときだけ働く"));
            container.Add(legacy);

            return container;
        }

        public static Func<bool> getHapticsEnabled;

        /** OGB（触覚デバイス連携）の設定のまとまり */
        public static VisualElement GetOgbHapticsSection(string key, Action<VisualElement> buildBody) {
            if (getHapticsEnabled == null) return new VisualElement();

            var hapticsEnabled = getHapticsEnabled();
            var haptics = VRCFuryEditorUtils.Group("触覚デバイス（OGB）", key,
                summary: () => hapticsEnabled ? null : "設定で OFF");
            if (!hapticsEnabled) {
                haptics.Add(VRCFuryEditorUtils.Info("触覚デバイスへの連携は、YozoLab SPS の設定（Tools/YozoLab SPS/Settings）で OFF になっている。"));
            }
            buildBody(haptics);
            return haptics;
        }

        public static VisualElement ConstraintWarning(UnityEngine.Component c, bool isSocket = false) {
            var reg = new VrcRegistryConfig();

            return VRCFuryEditorUtils.Debug(refreshElement: () => {
                var output = new VisualElement();
                var legacyRendererPaths = new List<string>();
                var tipLightPaths = new List<string>();
                var orificeLightPaths = new List<string>();

                foreach (var light in c.owner().GetComponentsInUploadRoot<Light>()) {
                    if (light.type != LightType.Point && light.type != LightType.Spot) continue;
                    var path = light.owner().GetDebugPath();
                    var type = SpsSocketEditor.GetLegacyDpsLightType(light);
                    if (type == SpsSocketEditor.LegacyDpsLightType.Tip)
                        tipLightPaths.Add(path);
                    else if (type == SpsSocketEditor.LegacyDpsLightType.Hole ||
                             type == SpsSocketEditor.LegacyDpsLightType.Ring ||
                             type == SpsSocketEditor.LegacyDpsLightType.Front)
                        orificeLightPaths.Add(path);
                }
                foreach (var renderer in c.owner().GetComponentsInUploadRoot<Renderer>()) {
                    foreach (var m in renderer.sharedMaterials) {
                        if (DpsConfigurer.IsDps(m) || TpsConfigurer.IsTps(m)) {
                            legacyRendererPaths.Add($"{m.name}（{renderer.owner().GetDebugPath()}）");
                        }
                    }
                }

                const string upgradeHint = "SPS への移し替えは Tools/YozoLab SPS/Upgrade DPS to SPS で行える。";
                if (tipLightPaths.Any()) {
                    output.Add(VRCFuryEditorUtils.Warn(
                        "DPS の先端ライトが残っている。SPS に移し替えきれておらず、近くに Socket が多いと不具合が出ることがある。" +
                        upgradeHint + "\n\n" + tipLightPaths.Join('\n')));
                }
                if (orificeLightPaths.Any()) {
                    output.Add(VRCFuryEditorUtils.Warn(
                        "SPS に移し替えていない DPS の穴のライトが残っている。同時に多く有効になると不具合が出ることがある。" +
                        upgradeHint + "\n\n" + orificeLightPaths.Join('\n')));
                }
                if (legacyRendererPaths.Any()) {
                    output.Add(VRCFuryEditorUtils.Warn(
                        "古い DPS / TPS の Plug が残っている。近くに Socket が多いと不具合が出ることがある。" +
                        upgradeHint + "\n\n" + legacyRendererPaths.Join('\n')));
                }

                if (c.owner().GetConstraints(true).Any()) {
                    output.Add(VRCFuryEditorUtils.Warn(
                        "この SPS コンポーネントは Constraint の中にある。できるだけ Constraint の中では使わないでください。" +
                        (isSocket ? "1 つの Socket を複数の場所で使い回すと、かえって重くなる。" : "")));
                }

                if (reg.TryGet("VRC_AV_INTERACT_SELF", out var val) && val != 1) {
                    output.Add(VRCFuryEditorUtils.Error(
                        "VRChat の設定で「Settings > Avatar > Avatar Interactions > Avatar Self Interact」を ON にしてください（SPS が正しく動かない）。"));
                }
                if (reg.TryGet("VRC_AV_INTERACT_LEVEL", out var val2) && val2 != 2) {
                    output.Add(VRCFuryEditorUtils.Warn(
                        "VRChat の設定「Settings > Avatar > Avatar Interactions > Avatar Allowed to Interact」が「Everyone」になっていない。" +
                        "他の人との間で SPS が働かないことがある。"));
                }
                if (reg.TryGet("PIXEL_LIGHT_COUNT", out var val3) && val3 != 3) {
                    output.Add(VRCFuryEditorUtils.Warn(
                        "VRChat の設定「Settings > Graphics > Advanced > Pixel Light Count」が「High」になっていない。" +
                        "ワールドによって SPS が正しく動かないことがある。"));
                }

                return output;
            });
        }

        private class GizmoCache {
            public double time = 0;
            public PlugSizeDetector.SizeResult size;
            public float[] radiusSamples;
            public string error;
            public Vector3 position;
            public Quaternion rotation;
        }

        private static readonly ConditionalWeakTable<SpsPlug, GizmoCache> gizmoCache
            = new ConditionalWeakTable<SpsPlug, GizmoCache>();
        
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        //[DrawGizmo(GizmoType.Selected | GizmoType.Active | GizmoType.InSelectionHierarchy)]
        static void DrawGizmo(SpsPlug plug, GizmoType gizmoType) {
            var transform = plug.owner();
            
            var cache = gizmoCache.GetOrCreateValue(plug);
            if (cache.time == 0 || transform.worldPosition != cache.position || transform.worldRotation != cache.rotation || EditorApplication.timeSinceStartup > cache.time + 1) {
                cache.time = EditorApplication.timeSinceStartup;
                cache.position = transform.worldPosition;
                cache.rotation = transform.worldRotation;
                cache.size = null;
                cache.radiusSamples = null;
                cache.error = null;
                try {
                    cache.size = PlugSizeDetector.GetWorldSize(plug);
                    if (cache.size != null) {
                        var worldPosition = transform.TransformPoint(cache.size.localPosition);
                        var worldRotation = transform.worldRotation * cache.size.localRotation;
                        cache.radiusSamples = SpsBaker.GetResolverRadiusSamples(
                            cache.size.renderers.Select(renderer => new SpsBaker.RendererBakeInput {
                                renderer = renderer,
                                activeFromMask = PlugMaskGenerator.GetMask(renderer, plug)
                            }),
                            worldPosition,
                            worldRotation,
                            cache.size.worldLength
                        );
                    }
                } catch (Exception e) {
                    cache.error = e.Message;
                }
            }

            var size = cache.size;
            Vector3 worldRoot;
            Vector3 worldForward;
            float worldLength;
            float worldRadius;
            Color color;
            string error = null;
            if (size == null) {
                worldRoot = transform.TransformPoint(Vector3.zero);
                worldForward = transform.TransformDirection(Vector3.forward);
                worldLength = 0.3f;
                worldRadius = 0.05f;
                color = Color.red;
                error = cache.error;
            } else {
                worldRoot = transform.TransformPoint(size.localPosition);
                worldForward = transform.TransformDirection(size.localRotation * Vector3.forward);
                worldLength = size.worldLength;
                worldRadius = size.worldRadius;
                color = new Color(1f, 0.5f, 0);
            }

            var isSelected = plug.owner().IsSelected();

            var worldEnd = worldRoot + worldForward * worldLength;
            VRCFuryGizmoUtils.DrawCappedCylinder(worldRoot, worldEnd, worldRadius, color);
            if (isSelected && size != null && cache.radiusSamples != null) {
                var sampleCount = cache.radiusSamples.Length;
                for (var i = 0; i < sampleCount; i++) {
                    var distance = worldLength * ((i + 0.5f) / sampleCount);
                    var sampleWorld = worldRoot + worldForward * distance;
                    VRCFuryGizmoUtils.DrawDisc(sampleWorld, worldForward, cache.radiusSamples[i], new Color(0.5f,0.5f,0.5f,0.4f));
                }
            }

            if (isSelected) {
                VRCFuryGizmoUtils.DrawText(
                    worldRoot + (worldEnd - worldRoot) / 2,
                    "SPS Plug" + (error == null ? "" : $"\n({error})"),
                    Color.gray,
                    true
                );
            }

            Gizmos.color = Color.clear;
            var gizmoStart = worldRoot;
            var gizmoEnd = worldEnd - worldForward * worldRadius;
            var gizmoCount = 5;
            for (var i = 0; i < gizmoCount; i++) {
                Gizmos.DrawSphere(gizmoStart + (gizmoEnd - gizmoStart) * i / (gizmoCount-1), worldRadius);
            }
        }

        public static ICollection<Renderer> GetRenderers(SpsPlug plug) {
            var renderers = new List<Renderer>();
            if (plug.autoRenderer) {
                renderers.AddRange(PlugRendererFinder.GetAutoRenderer(plug.owner(), !plug.useLegacyRendererFinder));
            } else {
                renderers.AddRange(plug.configureTpsMesh.Where(r => r != null));
            }
            return renderers;
        }

        public class BakeResult {
            public VFGameObject bakeRoot;
            public VFGameObject oneSpace;
            public VFGameObject worldSpace;
            public ICollection<RendererResult> renderers;
            public MeshRenderer resolverRenderer;
            public float worldLength;
            public float worldRadius;
            public List<SpsConfigurer.MaterialProperty> resolverMaterialProperties;
            public string oscId;
        }

        public class RendererResult {
            public Renderer renderer;
            public Func<int, Material, Material> configureMaterial;
            public IList<string> spsBlendshapes;
            public float[] activeFromMask;
        }
    }
}
