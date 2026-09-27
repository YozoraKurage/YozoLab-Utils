using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder.Haptics;
using YozoLab.SPS.Component;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Utils;

namespace YozoLab.SPS.Inspector {
    [CustomEditor(typeof(SpsSocket), true)]
    internal class SpsSocketEditor : VRCFuryComponentEditor<SpsSocket> {
        internal const int SpsTagCount = 2;
        internal const float GizmoRadiusOffset = 0.04f;
        internal const float LegacyRadiusOffset = 0.03f;
        private const int GuidedPathCount = 3;

        private void OnSceneGUI() {
            if (!(target is SpsSocket socket)) return;
            if (PrefabUtility.IsPartOfPrefabInstance(socket)) return;
            SpsSocketGizmoDrawer.DrawEditableTangents(socket);
            SpsSocketGizmoDrawer.DrawEditableLegacyOffset(socket);
        }

        internal static string GetMenuName(SpsSocket socket) {
            return HapticUtils.GetPreferredId(
                socket,
                s => s.name,
                s => HapticUtils.GetFallbackId(s.owner())
            );
        }

        private static string GetOscId(SpsSocket socket) {
            return HapticUtils.GetPreferredId(
                socket,
                s => s.oscId,
                _ => GetMenuName(socket)
            );
        }

        private static SerializedProperty AddSpsTag(SerializedProperty listProp) {
            if (listProp.arraySize >= SpsTagCount) return null;
            var index = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(index);
            var item = listProp.GetArrayElementAtIndex(index);
            item.stringValue = "";
            listProp.serializedObject.ApplyModifiedProperties();
            return item;
        }

        private static VisualElement SpsTagList(SerializedProperty listProp) {
            return VRCFuryEditorUtils.RefreshOnChange(() => {
                var container = new VisualElement();
                var (labelBox, _) = VRCFuryEditorUtils.CreateTooltip("追加のタグ", "このタグを狙うように設定した Plug も、この Socket を狙う");
                labelBox.AddToClassList("spsLabelOwnLine");
                container.Add(labelBox);
                for (var i = 0; i < Math.Min(listProp.arraySize, SpsTagCount); i++) {
                    var index = i;
                    var row = new VisualElement().Row().AlignItems(Align.Center);
                    row.AddToClassList("spsProp");
                    var field = SpsPlugEditor.SpsTagTextField(listProp.GetArrayElementAtIndex(index));
                    field.style.flexGrow = 1;
                    field.style.flexShrink = 1;
                    row.Add(field);
                    var remove = new Button(() => {
                        listProp.DeleteArrayElementAtIndex(index);
                        listProp.serializedObject.ApplyModifiedProperties();
                    }) {
                        text = "×",
                        tooltip = "このタグを消す"
                    };
                    remove.style.marginLeft = 6;
                    row.Add(remove);
                    container.Add(row);
                }
                if (listProp.arraySize < SpsTagCount) {
                    var add = new Button(() => AddSpsTag(listProp)) { text = "＋ タグを追加" };
                    add.style.alignSelf = Align.FlexStart;
                    add.AddToClassList("spsProp");
                    container.Add(add);
                }
                return container;
            }, listProp);
        }

        private static SerializedProperty AddGuidedPath(SerializedProperty listProp) {
            if (listProp.arraySize >= GuidedPathCount) return null;
            return VRCFuryEditorUtils.AddToList(listProp);
        }

        private static VisualElement GuidedPathList(SerializedProperty listProp) {
            var refreshProps = new List<SerializedProperty> { listProp };
            for (var i = 0; i < Math.Min(listProp.arraySize, GuidedPathCount); i++) {
                var item = listProp.GetArrayElementAtIndex(i);
                refreshProps.Add(item.FindPropertyRelative("customizeTangentOut"));
                refreshProps.Add(item.FindPropertyRelative("customizeTangentIn"));
            }
            return VRCFuryEditorUtils.RefreshOnChange(() => {
                var container = new VisualElement();

                string FormatSlot(int slot) {
                    if (slot == -1) return "入口";
                    return $"中継点 {slot + 1}";
                }

                for (var i = 0; i < Math.Min(listProp.arraySize, GuidedPathCount); i++) {
                    var index = i;
                    var item = listProp.GetArrayElementAtIndex(index);
                    var customizeTangentOut = item.FindPropertyRelative("customizeTangentOut");
                    var customizeTangentIn = item.FindPropertyRelative("customizeTangentIn");

                    var box = VRCFuryEditorUtils.Section(FormatSlot(index));
                    box.Add(VRCFuryEditorUtils.BetterProp(item.FindPropertyRelative("transform"), "位置",
                        tooltip: "この中継点になるオブジェクト"));
                    box.Add(VRCFuryEditorUtils.BetterProp(item.FindPropertyRelative("shrink"),
                        $"{FormatSlot(index - 1)}〜{FormatSlot(index)}の間で潰す"));
                    box.Add(VRCFuryEditorUtils.BetterProp(customizeTangentOut, $"{FormatSlot(index - 1)}から出る向きを指定"));
                    if (customizeTangentOut.boolValue) {
                        box.Add(VRCFuryEditorUtils.BetterProp(item.FindPropertyRelative("tangentOutLocal"), "向き"));
                    }
                    box.Add(VRCFuryEditorUtils.BetterProp(customizeTangentIn, $"{FormatSlot(index)}へ入る向きを指定"));
                    if (customizeTangentIn.boolValue) {
                        box.Add(VRCFuryEditorUtils.BetterProp(item.FindPropertyRelative("tangentInLocal"), "向き"));
                    }
                    var remove = new Button(() => {
                        listProp.DeleteArrayElementAtIndex(index);
                        listProp.serializedObject.ApplyModifiedProperties();
                    }) {
                        text = "この中継点を消す"
                    };
                    remove.style.alignSelf = Align.FlexEnd;
                    box.Add(remove);
                    container.Add(box);
                }
                if (listProp.arraySize < GuidedPathCount) {
                    var add = new Button(() => AddGuidedPath(listProp)) { text = "＋ 中継点を追加" };
                    add.style.alignSelf = Align.FlexStart;
                    container.Add(add);
                }

                return container;
            }, refreshProps.ToArray());
        }

        private static readonly string[] ModeNames = { "自動", "穴", "輪", "片側だけの輪（まれ）" };

        protected override VisualElement CreateEditor(SerializedObject serializedObject, SpsSocket target) {
            var container = new VisualElement();

            var notices = new VisualElement();
            notices.AddToClassList("spsNotices");
            notices.Add(SpsPlugEditor.ConstraintWarning(target, true));
            container.Add(notices);

            // ---- 変形 ----
            var addLightProp = serializedObject.FindProperty("addLight");
            var noneIndex = (int)SpsSocket.AddLight.None;
            var autoIndex = (int)SpsSocket.AddLight.Auto;
            var spsSection = VRCFuryEditorUtils.Section("変形（SPS）", "SPS の Plug がこの Socket に向かって曲がる。入口はこのオブジェクトの +Z 側");
            container.Add(spsSection);

            var spsEnabledCheckbox = new Toggle();
            spsEnabledCheckbox.SetValueWithoutNotify(addLightProp.enumValueIndex != noneIndex);
            spsEnabledCheckbox.RegisterValueChangedCallback(cb => {
                addLightProp.enumValueIndex = cb.newValue ? autoIndex : noneIndex;
                addLightProp.serializedObject.ApplyModifiedProperties();
            });
            spsSection.Add(VRCFuryEditorUtils.BetterProp(addLightProp, "Plug を受け入れる", fieldOverride: spsEnabledCheckbox,
                tooltip: "切ると、子にある古い DPS のライトがあればそれに従う"));
            spsSection.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                var output = new VisualElement();
                if (addLightProp.enumValueIndex == noneIndex) return output;

                // enum の並び: None, Hole, Ring, Auto, RingOneWay
                var index = addLightProp.enumValueIndex;
                var modeField = new PopupField<string>(
                    ModeNames.ToList(),
                    index == (int)SpsSocket.AddLight.RingOneWay ? 3
                        : index == (int)SpsSocket.AddLight.Ring ? 2
                        : index == (int)SpsSocket.AddLight.Hole ? 1
                        : 0
                );
                modeField.RegisterValueChangedCallback(cb => {
                    var i = ModeNames.ToList().IndexOf(cb.newValue);
                    addLightProp.enumValueIndex = i == 1 ? (int)SpsSocket.AddLight.Hole
                        : i == 2 ? (int)SpsSocket.AddLight.Ring
                        : i == 3 ? (int)SpsSocket.AddLight.RingOneWay
                        : autoIndex;
                    addLightProp.serializedObject.ApplyModifiedProperties();
                });
                output.Add(VRCFuryEditorUtils.BetterProp(addLightProp, "形", fieldOverride: modeField,
                    tooltip: "穴: 奥で止まる。\n" +
                             "輪: 通り抜けられる（SPS なら裏からも入れる。TPS / DPS は片側だけ）。\n" +
                             "片側だけの輪: 表からだけ通り抜けられる。\n" +
                             "自動: 腰か頭に付いていれば穴、それ以外は輪。"));
                output.Add(VRCFuryEditorUtils.BetterProp(
                    serializedObject.FindProperty("useRadiusOffset"),
                    "Plug の太さぶん上にずらす",
                    tooltip: "狙う位置を、Plug の半径だけ Socket の上方向にずらす。古い形式のライトも、TPS / DPS に合わせて少し上に動く"
                ));
                output.Add(VRCFuryEditorUtils.BetterProp(
                    serializedObject.FindProperty("guidedPathStops"),
                    "通り道（中継点）",
                    fieldOverride: GuidedPathList(serializedObject.FindProperty("guidedPathStops")),
                    tooltip: "入口を通った後、ここに挙げたオブジェクトを順にたどって曲がる。穴なら、最後の中継点で潰れる"
                ));

                var useLights = serializedObject.FindProperty("useLights");
                output.Add(VRCFuryEditorUtils.BetterProp(useLights, "古い形式の Plug も受け入れる",
                    tooltip: "ライトを置いて、SPS1 / DPS / TPS の Plug も曲がるようにする"));
                output.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                    if (!useLights.boolValue) return new VisualElement();
                    var legacy = new VisualElement();
                    legacy.style.marginLeft = 18;
                    var overrideLegacySocketType = serializedObject.FindProperty("overrideLegacySocketType");
                    legacy.Add(VRCFuryEditorUtils.BetterProp(overrideLegacySocketType, "古い形式での形を別に指定"));
                    legacy.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                        if (!overrideLegacySocketType.boolValue) return new VisualElement();
                        return VRCFuryEditorUtils.Prop(
                            serializedObject.FindProperty("legacySocketType"),
                            "形",
                            formatEnum: FormatLegacyType
                        ).AddClass("spsProp");
                    }, overrideLegacySocketType));
                    var overrideLegacyOffset = serializedObject.FindProperty("overrideLegacyOffset");
                    legacy.Add(VRCFuryEditorUtils.BetterProp(overrideLegacyOffset, "古い形式での入口の位置を別に指定"));
                    legacy.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                        if (!overrideLegacyOffset.boolValue) return new VisualElement();
                        return VRCFuryEditorUtils.BetterProp(
                            serializedObject.FindProperty("legacyOffsetLocal"),
                            "位置のずれ"
                        );
                    }, overrideLegacyOffset));
                    return legacy;
                }, useLights));

                return output;
            }, addLightProp));

            // ---- メニュー ----
            var menu = VRCFuryEditorUtils.Section("メニュー");
            container.Add(menu);
            var addMenuItemProp = serializedObject.FindProperty("addMenuItem");
            menu.Add(VRCFuryEditorUtils.BetterProp(addMenuItemProp, "メニューにオン/オフを作る",
                tooltip: "SPS のメニューに、この Socket を有効にする項目を足す"));
            menu.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                if (!addMenuItemProp.boolValue) return new VisualElement();
                var toggles = new VisualElement();
                toggles.Add(SpsEditorUtils.AutoHapticIdProp(
                    serializedObject.FindProperty("name"),
                    "メニューでの名前",
                    target,
                    target.owner(),
                    avatar => avatar.GetComponentsInSelfAndChildren<SpsSocket>(),
                    GetMenuName
                ));
                toggles.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("enableAuto"), "「自動」の候補にする",
                    tooltip: "メニューの「自動」を選んだとき、Plug にいちばん近い Socket を自動で有効にする。その候補に入れる"));
                toggles.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("menuIcon"), "アイコン",
                    tooltip: "この Socket の項目のアイコン。SPS のメニュー全体の場所やアイコンは、アバターのルートの「SPS Options」で変える"));
                return toggles;
            }, addMenuItemProp));

            // ---- 深度アニメーション ----
            var depthList = serializedObject.FindProperty("depthActions2");
            var depth = VRCFuryEditorUtils.Group("深度アニメーション", "socket.depth",
                defaultOpen: depthList.arraySize > 0,
                summary: () => depthList.arraySize > 0 ? $"{depthList.arraySize} 件" : "なし");
            depth.Add(VRCFuryEditorUtils.WrappedLabel("Plug がどこまで入っているかに応じて、好きなものを動かす").AddClass("spsSubtitle"));
            depth.Add(VRCFuryEditorUtils.List(depthList));
            container.Add(depth);

            // ---- 有効なときのアニメーション ----
            var activeList = serializedObject.FindProperty("activeActions.actions");
            var active = VRCFuryEditorUtils.Group("有効なときのアニメーション", "socket.active",
                defaultOpen: activeList.arraySize > 0,
                summary: () => activeList.arraySize > 0 ? $"{activeList.arraySize} 件" : "なし");
            active.Add(VRCFuryEditorUtils.WrappedLabel("メニューでこの Socket を有効にしている間、ずっと適用する").AddClass("spsSubtitle"));
            active.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("activeActions")));
            container.Add(active);

            // ---- 狙われる条件 ----
            var useSharedTag = serializedObject.FindProperty("useSharedTag");
            var tagsProp = serializedObject.FindProperty("tags");
            var tags = VRCFuryEditorUtils.Group("狙う Plug の条件（タグ）", "socket.tags",
                summary: () => !useSharedTag.boolValue ? "共通タグなし" : tagsProp.arraySize > 0 ? "タグあり" : "既定");
            tags.Add(VRCFuryEditorUtils.BetterProp(useSharedTag, "共通の SPS タグを持つ",
                tooltip: "ふつうの設定の Plug は、このタグを持つ Socket を狙う"));
            tags.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                if (!useSharedTag.boolValue) {
                    return VRCFuryEditorUtils.Warn("共通の SPS タグを持たないので、ほとんどの Plug はこの Socket を狙わない。");
                }
                var autoTags = VRCFuryPerFrameInjector.GetPerFrameInjector(target.owner())
                    .GetService<SpsConfigurer>()
                    .GetAutoSocketTagNames(target);
                if (autoTags.Count > 0) {
                    return VRCFuryEditorUtils.Info("付いている場所から、自動でこのタグも持つ: " + autoTags.Join(", "));
                }
                return new VisualElement();
            }, useSharedTag));
            tags.Add(SpsTagList(tagsProp));
            container.Add(tags);

            // ---- 触覚 ----
            container.Add(SpsPlugEditor.GetOgbHapticsSection("socket.haptics", haptics => {
                haptics.Add(SpsEditorUtils.AutoHapticIdProp(
                    serializedObject.FindProperty("oscId"),
                    "OGB に送る名前",
                    target,
                    target.owner(),
                    avatar => avatar.GetComponentsInSelfAndChildren<SpsSocket>(),
                    GetOscId
                ));
                haptics.Add(VRCFuryEditorUtils.Prop(
                    serializedObject.FindProperty("enableHandTouchZone2"),
                    "手で触れる範囲",
                    formatEnum: FormatTouchZone,
                    tooltip: "手で触れたことを触覚デバイスへ送る範囲。自動なら腰に付いているときだけ作る"
                ).AddClass("spsProp"));
                haptics.Add(VRCFuryEditorUtils.BetterProp(
                    serializedObject.FindProperty("length"),
                    "手で触れる範囲の深さ",
                    tooltip: "メートル。0 なら自動。手で触れる判定だけに使い、Plug には関係しない"
                ));
            }));

            // ---- 詳細 ----
            var adv = VRCFuryEditorUtils.Group("詳細", "socket.advanced");
            adv.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("position"), "入口の位置のずれ",
                tooltip: "このオブジェクトからの位置のずれ（ローカル）"));
            adv.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("rotation"), "入口の向きのずれ",
                tooltip: "このオブジェクトからの回転のずれ（ローカル、度）"));
            adv.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("unitsInMeters"), "長さをワールド単位（メートル）で扱う"));
            var plugParams = VRCFuryEditorUtils.Section("Plug の大きさをパラメーターに出す（非推奨）",
                "代わりにアニメーターへのパラメーター注入を使ってください");
            plugParams.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("enablePlugLengthParameter"), "長さ（メートル）"));
            plugParams.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("plugLengthParameterName"), "パラメーター名"));
            plugParams.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("enablePlugWidthParameter"), "半径（メートル）"));
            plugParams.Add(VRCFuryEditorUtils.BetterProp(serializedObject.FindProperty("plugWidthParameterName"), "パラメーター名"));
            adv.Add(plugParams);
            container.Add(adv);

            return container;
        }

        private static string FormatLegacyType(string name) {
            switch (name) {
                case "Hole": return "穴";
                case "Ring": return "輪";
                case "Ring One Way": return "片側だけの輪";
                default: return name;
            }
        }

        private static string FormatTouchZone(string name) {
            switch (name) {
                case "Auto": return "自動";
                case "On": return "作る";
                case "Off": return "作らない";
                default: return name;
            }
        }

        [CustomPropertyDrawer(typeof(SpsSocket.DepthActionNew))]
        public class DepthActionDrawer : PropertyDrawer {
            public override VisualElement CreatePropertyGUI(SerializedProperty prop) {
                var c = new VisualElement();
                c.Add(VRCFuryEditorUtils.BetterProp(prop.FindPropertyRelative("actionSet")));
                var units = prop.FindPropertyRelative("units");
                c.Add(VRCFuryEditorUtils.RefreshOnChange(() =>
                    VRCFuryEditorUtils.BetterProp(
                        null,
                        "動かす範囲",
                        tooltip: "遠い側から動き始め、近い側で最大になる。動きの無いアクションやクリップなら、" +
                                 "遠い側で完全に OFF、近い側で完全に ON になる",
                        fieldOverride: new DepthActionSlider(prop.FindPropertyRelative("range"), (SpsSocket.DepthActionUnits)units.enumValueIndex)
                    )
                , units));
                c.Add(VRCFuryEditorUtils.Prop(units, "範囲の単位", formatEnum: FormatUnits).AddClass("spsProp"));
                c.Add(VRCFuryEditorUtils.BetterProp(prop.FindPropertyRelative("enableSelf"), "自分のアバターでも動かす"));
                c.Add(VRCFuryEditorUtils.BetterProp(prop.FindPropertyRelative("smoothingSeconds"), "なめらかにする秒数",
                    tooltip: "目標の深さへ、だいたいこの秒数をかけて近づく。フレームレートに依存するので、FPS が高いほど速くなる"));
                c.Add(VRCFuryEditorUtils.BetterProp(prop.FindPropertyRelative("reverseClip"), "クリップを逆再生する（まれ）"));
                return c;
            }

            private static string FormatUnits(string name) {
                switch (name) {
                    case "Meters": return "メートル";
                    case "Plugs": return "Plug の長さ";
                    case "Local": return "ローカル単位";
                    default: return name;
                }
            }
        }

        public enum LegacyDpsLightType {
            None,
            Hole,
            Ring,
            Front,
            Tip
        }
        public static LegacyDpsLightType GetLegacyDpsLightType(Light light) {
            if (light.range >= 0.5) return LegacyDpsLightType.None; // Outside of range
            var secondDecimal = (int)Math.Round((light.range % 0.1) * 100);
            if ((light.color.maxColorComponent > 1 && light.color.a > 0)) return LegacyDpsLightType.None; // For some reason, dps tip lights are (1,1,1,255)
            if (secondDecimal == 9 || secondDecimal == 8) return LegacyDpsLightType.Tip;
            if ((light.color.maxColorComponent > 0 && light.color.a > 0)) return LegacyDpsLightType.None; // Visible light
            if (secondDecimal == 1 || secondDecimal == 3) return LegacyDpsLightType.Hole;
            if (secondDecimal == 2 || secondDecimal == 4) return LegacyDpsLightType.Ring;
            if (secondDecimal == 5 || secondDecimal == 6) return LegacyDpsLightType.Front;
            return LegacyDpsLightType.None;
        }

        internal static SpsSocket.AddLight GetGuidedPathTerminalType(SpsSocket.AddLight lightType) {
            return lightType == SpsSocket.AddLight.Hole
                ? SpsSocket.AddLight.Hole
                : SpsSocket.AddLight.RingOneWay;
        }

        internal static SpsSocket.AddLight GetLegacyLightType(SpsSocket socket, SpsSocket.AddLight lightType) {
            if (socket.overrideLegacySocketType) {
                return socket.legacySocketType switch {
                    SpsSocket.LegacySocketType.Hole => SpsSocket.AddLight.Hole,
                    SpsSocket.LegacySocketType.RingOneWay => SpsSocket.AddLight.RingOneWay,
                    _ => SpsSocket.AddLight.Ring
                };
            }
            return socket.guidedPathStops.Any(stop => stop != null && stop.transform != null)
                ? SpsSocket.AddLight.Hole
                : lightType;
        }

        /**
         * Visit every light that could possibly be used for this socket. This includes all children,
         * and single-depth children of all parents.
         */
        public static void ForEachPossibleLight(VFGameObject obj, bool directOnly, Action<Light> act) {
            var visited = new HashSet<Light>();
            void Visit(Light light) {
                if (visited.Contains(light)) return;
                visited.Add(light);
                var type = GetLegacyDpsLightType(light);
                if (type != LegacyDpsLightType.Hole && type != LegacyDpsLightType.Ring && type != LegacyDpsLightType.Front) return;
                act(light);
            }
            foreach (var child in obj.Children()) {
                foreach (var light in child.GetComponents<Light>()) {
                    Visit(light);
                }
            }
            if (!directOnly) {
                foreach (var light in obj.GetComponentsInSelfAndChildren<Light>()) {
                    Visit(light);
                }
            }
        }
        public static Tuple<SpsSocket.AddLight, Vector3, Quaternion> GetInfoFromLights(VFGameObject obj, bool directOnly = false) {
            var isRing = false;
            Light main = null;
            Light front = null;
            ForEachPossibleLight(obj, directOnly, light => {
                var type = GetLegacyDpsLightType(light);
                if (main == null) {
                    if (type == LegacyDpsLightType.Hole) {
                        main = light;
                    } else if (type == LegacyDpsLightType.Ring) {
                        main = light;
                        isRing = true;
                    }
                }
                if (front == null && type == LegacyDpsLightType.Front) {
                    front = light;
                }
            });

            if (main == null || front == null) return null;

            var position = obj.InverseTransformPoint(main.owner().worldPosition);
            var frontPosition = obj.InverseTransformPoint(front.owner().worldPosition);
            var forward = (frontPosition - position).normalized;
            var rotation = Quaternion.LookRotation(forward);

            return Tuple.Create(isRing ? SpsSocket.AddLight.Ring : SpsSocket.AddLight.Hole, position, rotation);
        }

        public static Tuple<SpsSocket.AddLight, TransformData> GetSocketTransformLocal(
            SpsSocket socket
        ) {
            if (socket.addLight != SpsSocket.AddLight.None) {
                return Tuple.Create(
                    socket.addLight,
                    new TransformData(socket.position, Quaternion.Euler(socket.rotation))
                );
            }

            var lightInfo = GetInfoFromLights(socket.owner());
            return lightInfo == null
                ? Tuple.Create(
                    SpsSocket.AddLight.None,
                    new TransformData(Vector3.zero, Quaternion.identity)
                )
                : Tuple.Create(
                    lightInfo.Item1,
                    new TransformData(lightInfo.Item2, lightInfo.Item3)
                );
        }

        public class BakeResult {
            public VFGameObject bakeRoot;
            public VFGameObject oneSpace;
            public VFGameObject worldSpace;
            public List<VFGameObject> screenMarkers;
            public List<ScreenMarkerResult> screenMarkerResults;
            public VFGameObject lights;
            public VFGameObject senders;
        }

        public class ScreenMarkerResult {
            public VFGameObject obj;
            public MeshRenderer renderer;
            public List<SpsConfigurer.MaterialProperty> materialProperties;
        }
    }
}
