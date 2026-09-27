using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Utils;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Inspector {

    internal static class VRCFuryEditorUtils {
        public static VisualElement List(
            SerializedProperty list,
            Action onPlus = null,
            Func<VisualElement> onEmpty = null
        ) {
            var output = new VisualElement();
            output.AddToClassList("vfList");

            if (list == null) {
                return Error("リストが見つからない");
            }
            if (!list.isArray) {
                return Error("リストではない");
            }

            void OnClickPlus() {
                if (onPlus != null) {
                    onPlus();
                } else {
                    AddToList(list);
                }
            }

            void OnClickMinus() {
                if (list.arraySize == 0) {
                } else if (list.arraySize == 1) {
                    list.DeleteArrayElementAtIndex(0);
                    list.serializedObject.ApplyModifiedProperties();
                } else {
                    DialogUtils.DisplayDialog("YozoLab SPS", "消したい項目を右クリックしてください", "OK");
                }
            }

            void Move(int offset, int pos) {
                if (pos < 0 || pos >= list.arraySize) return;
                list.MoveArrayElement(offset, pos);
                list.serializedObject.ApplyModifiedProperties();
                output.Bind(list.serializedObject);
            }

            void CreateRightClickMenu(VisualElement el) {
                el.AddManipulator(new ContextualMenuManipulator(e => {
                    var offset = (int)el.userData;
                    if (e.menu.MenuItems().Count > 0) {
                        e.menu.AppendSeparator();
                    }
                    e.menu.AppendAction("削除", a => {
                        list.DeleteArrayElementAtIndex(offset);
                        list.serializedObject.ApplyModifiedProperties();
                    });
                    e.menu.AppendSeparator();
                    var disabledIfTop = offset == 0 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
                    var disabledIfBottom = offset == list.arraySize - 1 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
                    e.menu.AppendAction("上へ", a => {
                        Move(offset, offset - 1);
                    }, disabledIfTop);
                    e.menu.AppendAction("下へ", a => {
                        Move(offset, offset+1);
                    }, disabledIfBottom);
                    e.menu.AppendSeparator();
                    e.menu.AppendAction("一番上へ", a => {
                        Move(offset, 0);
                    }, disabledIfTop);
                    e.menu.AppendAction("一番下へ", a => {
                        Move(offset, list.arraySize-1);
                    }, disabledIfBottom);
                    e.StopPropagation();
                }));
            }

#if UNITY_2021_2_OR_NEWER
            var listView = new ListView();
            listView.AddToClassList("vfList__listView");
            listView.reorderable = true;
            listView.reorderMode = ListViewReorderMode.Animated;
            listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            listView.showBorder = true;
            listView.showAddRemoveFooter = false;
            listView.bindingPath = list.propertyPath;
            listView.showBoundCollectionSize = false;
            listView.selectionType = SelectionType.None;
            listView.showAlternatingRowBackgrounds = AlternatingRowBackground.All;

            listView.makeItem = () => {
                var field = new PropertyField();
                CreateRightClickMenu(field);
                return field;
            };
            listView.bindItem = (element, i) => {
                ((PropertyField)element).bindingPath = list.GetArrayElementAtIndex(i).propertyPath;
                element.Bind(list.serializedObject);
                element.userData = i;
            };
            listView.unbindItem = (element, i) => {
                element.Unbind();
            };

            var footer = new VisualElement() {
                name = BaseListView.footerUssClassName
            };
            footer.AddToClassList(BaseListView.footerUssClassName);
            footer.Add(new Button(OnClickMinus) {
                name = BaseListView.footerRemoveButtonName,
                text = "-"
            });
            footer.Add(new Button(OnClickPlus) {
                name = BaseListView.footerAddButtonName,
                text = "+"
            });

            output.Add(listView);
            output.Add(footer);
#else
            var entriesContainer = new VisualElement()
                .Border(1)
                .BorderColor(Color.black)
                .BorderRadius(5);
            output.Add(entriesContainer);
            entriesContainer.style.backgroundColor = new Color(0,0,0,0.1f);
            entriesContainer.style.minHeight = 20;

            entriesContainer.Add(RefreshOnChange(() => {
                var entries = new VisualElement();
                var size = list.arraySize;
                for (var i = 0; i < size; i++) {
                    var offset = i;
                    var el = list.GetArrayElementAtIndex(i);
                    var row = new VisualElement {
                        style = {
                            flexDirection = FlexDirection.Row
                        }
                    };
                    row.AddToClassList("vfListRow");
                    if (offset != size - 1) {
                        row.AddToClassList("vfList2019__notLastItem");
                    }
                    row.style.alignItems = Align.FlexStart;
                    entries.Add(row);

                    VisualElement data = Prop(el).Padding(5);
                    data.AddToClassList("vfListRowData");
                    data.style.flexGrow = 1;
                    row.Add(data);

                    row.userData = i;
                    CreateRightClickMenu(row);

                    data.AddToClassList("vfListRowButtons");
                }
                if (size == 0) {
                    if (onEmpty != null) {
                        entries.Add(onEmpty());
                    } else {
                        var label = WrappedLabel("まだ何もない。+ で追加する").Padding(5);
                        label.style.unityTextAlign = TextAnchor.MiddleCenter;
                        entries.Add(label);
                    }
                }
                return entries;
            }, list));

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            output.Add(buttonRow);

            var buttonSpacer = new VisualElement();
            buttonRow.Add(buttonSpacer);
            buttonSpacer.style.flexGrow = 1;

            entriesContainer.style.borderBottomRightRadius = 0;
            var buttons = new VisualElement();
            buttons.AddToClassList("vfList2019__buttons");
            buttonRow.Add(buttons);

            var add = new Label("+");
            add.AddToClassList("vfList2019__button");
            add.AddManipulator(new Clickable(OnClickPlus));
            buttons.Add(add);
            
            var subtract = new Label("-");
            subtract.AddToClassList("vfList2019__button");
            subtract.AddManipulator(new Clickable(OnClickMinus));
            buttons.Add(subtract);
#endif
            return output;
        }

        public static SerializedProperty AddToList(SerializedProperty list, Action<SerializedProperty> doWith = null) {
            list.serializedObject.Update();
            list.InsertArrayElementAtIndex(list.arraySize);
            var newEntry = list.GetArrayElementAtIndex(list.arraySize-1);
            list.serializedObject.ApplyModifiedProperties();

            // InsertArrayElementAtIndex makes a copy of the last element for some reason, instead of a fresh copy
            // We fix that here by finding the raw array and creating a fresh object for the new element
            if (newEntry.propertyType == SerializedPropertyType.ManagedReference) {
                newEntry.managedReferenceValue = null;
                list.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            } else {
                if (list.GetObject() is IList listObj) {
                    var type = listObj[listObj.Count - 1].GetType();
                    if (type == typeof(string)) {
                        listObj[listObj.Count - 1] = "";
                    } else {
                        listObj[listObj.Count - 1] = Activator.CreateInstance(type);
                    }
                    list.serializedObject.Update();
                } else {
                    UnityEngine.Debug.LogError("Failed to find list to reset new entry.");
                }
            }

            if (doWith != null) {
                doWith(newEntry);
                list.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            return newEntry;
        }

        public static Label WrappedLabel(string text) {
            return new Label(text).TextWrap();
        }
        
        public static VisualElement BetterProp(
            SerializedProperty prop,
            string label = null,
            string tooltip = null,
            VisualElement fieldOverride = null,
            string placeholder = null
        ) {
            var el = Prop(prop, label, tooltip: tooltip, fieldOverride: fieldOverride, placeholder: placeholder);
            el.AddToClassList("spsProp");
            return el;
        }

        public static VisualElement AutoIdProp(SerializedProperty prop, string label, Func<string> getAutomaticId) {
            var textField = new TextFieldWithPlaceholder {
                bindingPath = prop.propertyPath
            };

            void RefreshAutoId() {
                textField.Placeholder = "自動（" + getAutomaticId() + "）";
            }

            textField.RegisterValueChangedCallback(_ => {
                RefreshAutoId();
            });

            var output = Prop(
                prop,
                label,
                fieldOverride: textField,
                onChange: RefreshAutoId
            );
            output.AddToClassList("spsProp");
            RefreshAutoId();
            return output;
        }

        /**
         * 項目名と、その説明。説明はマウスを重ねたときに出す（項目名の横に小さな「?」を付ける）。
         * 2 つ目の戻り値は、以前は項目名をクリックで開く説明の箱だったもので、今は常に null。
         */
        public static (VisualElement, VisualElement) CreateTooltip(string label, string content) {
            if (label == null) return (null, null);
            var labelText = WrappedLabel(label);
            labelText.AddToClassList("spsLabel");
            if (content == null) return (labelText, null);

            var labelBox = new VisualElement().Row();
            labelBox.AddToClassList("spsLabelBox");
            labelBox.tooltip = content;
            labelBox.Add(labelText);
            var help = new Image {
                image = EditorGUIUtility.FindTexture("_Help"),
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = content
            };
            help.AddToClassList("spsHelpIcon");
            labelBox.Add(help);
            return (labelBox, null);
        }

        public static VisualElement Prop(
            SerializedProperty prop,
            string label = null,
            int labelWidth = 100,
            Func<string,string> formatEnum = null,
            string tooltip = null,
            VisualElement fieldOverride = null,
            bool forceLabelOnOwnLine = false,
            Action onChange = null,
            string placeholder = null
        ) {
            VisualElement field = null;
            var isCheckbox = false;
            if (fieldOverride != null) {
                field = fieldOverride;
                isCheckbox = field is Toggle;
                if (placeholder != null && field is TextFieldWithPlaceholder textFieldOverride) {
                    textFieldOverride.Placeholder = placeholder;
                }
            } else if (prop == null) {
                field = WrappedLabel("Prop is null");
            } else {
                switch (prop.propertyType) {
                    case SerializedPropertyType.Vector4: {
                        field = new Vector4Field { bindingPath = prop.propertyPath };
                        break;
                    }
                    case SerializedPropertyType.Enum: {
                        field = new PopupField<string>(
                            prop.enumDisplayNames.ToList(),
                            prop.enumValueIndex,
                            formatSelectedValueCallback: formatEnum,
                            formatListItemCallback: formatEnum
                        ) { bindingPath = prop.propertyPath };
                        break;
                    }
                    case SerializedPropertyType.Boolean: {
                        var toggle = new Toggle { bindingPath = prop.propertyPath };
                        toggle.RegisterValueChangedCallback(e => {
                            onChange?.Invoke();
                        });
                        field = toggle;
                        break;
                    }
                    case SerializedPropertyType.Color: {
                        field = new ColorField { bindingPath = prop.propertyPath, hdr = true };
                        break;
                    }
                    case SerializedPropertyType.ObjectReference: {
                        var objectField = new ObjectField { bindingPath = prop.propertyPath };
                        objectField.objectType = GetPropertyType(prop);
                        field = objectField;
                        break;
                    }
                    case SerializedPropertyType.String: {
                        if (placeholder != null) {
                            var textField = new TextFieldWithPlaceholder {
                                bindingPath = prop.propertyPath,
                                Placeholder = placeholder
                            };
                            textField.RegisterValueChangedCallback(e => {
                                onChange?.Invoke();
                            });
                            field = textField;
                        }
                        break;
                    }
                    case SerializedPropertyType.Generic: {
                        if (prop.type == "State") {
                            return VRCFuryActionSetDrawer.render(prop, label, labelWidth, tooltip);
                        }

                        break;
                    }
                }
                if (field == null) {
                    field = new PropertyField(prop);
                }
                isCheckbox = prop.propertyType == SerializedPropertyType.Boolean;
            }

            field.AddToClassList("VrcFuryEditorProp");

            return AssembleProp(
                label,
                tooltip,
                field,
                isCheckbox,
                forceLabelOnOwnLine,
                labelWidth
            );
        }

        public static VisualElement AssembleProp(
            string label,
            string tooltip,
            VisualElement field,
            bool isCheckbox,
            bool forceLabelOnOwnLine,
            int labelWidth
        ) {
            var (labelBox, tooltipBox) = CreateTooltip(label, tooltip);
            var wrapper = new VisualElement();
            wrapper.AddToClassList("spsField");

            // リストや入れ子の設定など、1 行に収まらない物は項目名を上に置く
            var isBlock = field != null && !(field is BindableElement) && !(field is PropertyField) && !isCheckbox;

            if (labelBox == null || field == null) {
                if (labelBox != null) wrapper.Add(labelBox);
                if (field != null) wrapper.Add(field);
            } else if (isCheckbox) {
                // チェックボックスは左に箱、右に項目名（クリックでも切り替わる）
                var row = new VisualElement().Row().FlexShrink(0);
                row.AddToClassList("spsCheckboxRow");
                field.AddToClassList("spsCheckbox");
                row.Add(field);
                labelBox.style.flexShrink = 1;
                labelBox.style.flexGrow = 1;
                if (field is Toggle toggle) {
                    labelBox.AddManipulator(new Clickable(() => {
                        if (toggle.enabledInHierarchy) toggle.value = !toggle.value;
                    }));
                }
                row.Add(labelBox);
                wrapper.Add(row);
            } else if (forceLabelOnOwnLine || isBlock) {
                labelBox.AddToClassList("spsLabelOwnLine");
                wrapper.Add(labelBox);
                wrapper.Add(field);
            } else {
                var labelRow = new VisualElement().Row();
                labelBox.AddToClassList("spsLabelColumn");
                labelRow.Add(labelBox);
                field.style.flexGrow = 1;
                field.style.flexShrink = 1;
                field.style.minWidth = 0;
                labelRow.Add(field);
                wrapper.Add(labelRow);
            }

            if (tooltipBox != null) wrapper.Add(tooltipBox);
            return wrapper;
        }

        public static VisualElement OnChange(SerializedProperty prop, Action changed) {

            var c = changed;
            changed = () => {
                // Unity sometimes calls onchange when the SerializedProperty is no longer valid.
                // Unfortunately the only way to detect this is to try to access it and catch an error, since isValid is internal
                try {
                    var name = prop.name;
                } catch (Exception) {
                    return;
                }
                c();
            };

            switch(prop.propertyType) {
                case SerializedPropertyType.Boolean:
                    return _OnChange(prop, () => prop.boolValue, changed, (a,b) => a==b);
                case SerializedPropertyType.Integer:
                    return _OnChange(prop, () => prop.intValue, changed, (a,b) => a==b);
                case SerializedPropertyType.String:
                    return _OnChange(prop, () => prop.stringValue, changed, (a,b) => a==b);
                case SerializedPropertyType.ObjectReference:
                    return _OnChange(prop, () => prop.objectReferenceValue, changed, (a,b) => a==b);
                case SerializedPropertyType.Enum:
                    return _OnChange(prop, () => prop.enumValueIndex, changed, (a,b) => a==b);
            }

            if (prop.isArray) {
                var fakeField = new IntegerField();
                fakeField.bindingPath = prop.propertyPath+".Array.size";
                fakeField.SetVisible(false);
                var oldValue = prop.arraySize;
                fakeField.RegisterValueChangedCallback(e => {
                    if (prop.arraySize == oldValue) return;
                    oldValue = prop.arraySize;
                    //Debug.Log("Detected change in " + prop.propertyPath);
                    changed();
                });
                return fakeField;
            }
            throw new Exception("Type " + prop.propertyType + " not supported (yet) by OnChange");
        }
        private static VisualElement _OnChange<T>(SerializedProperty prop, Func<T> getValue, Action changed, Func<T,T,bool> equals) {
            // The register events can sometimes randomly fire when binding / unbinding happens,
            // with the oldValue being "null", so we have to do our own change detection by caching the old value.
            var fakeField = new PropertyField(prop).SetVisible(false);
        
            var oldValue = getValue();
            void Check() {
                var newValue = getValue();
                if (equals(oldValue, newValue)) return;
                oldValue = newValue;
                //Debug.Log("Detected change in " + prop.propertyPath);
                changed();
            }
            if (prop.propertyType == SerializedPropertyType.Enum) {
                fakeField.RegisterCallback<ChangeEvent<string>>(e => changed());
            } else {
                fakeField.RegisterCallback<ChangeEvent<T>>(e => Check());
            }

            return fakeField;
        }
        
        public static VisualElement RefreshOnTrigger(Func<VisualElement> content, SerializedObject obj, out Action triggerRefresh) {
            var inner = new VisualElement();
            inner.Add(content());

            void Refresh() {
                inner.Unbind();
                inner.Clear();
                var newContent = content();
                inner.Add(newContent);
                inner.Bind(obj);
            }

            triggerRefresh = Refresh;
            return inner;
        }

        public static VisualElement RefreshOnChange(Func<VisualElement> content, params SerializedProperty[] props) {
            var container = new VisualElement();
            if (props.Length == 0 || props.Any(p => p == null))
                throw new Exception("RefreshOnChange received null prop");
            container.Add(RefreshOnTrigger(content, props[0].serializedObject, out var triggerRefresh));
            foreach (var prop in props) {
                if (prop != null) {
                    var onChangeField = OnChange(prop, triggerRefresh);
                    container.Add(onChangeField);
                }
            }
            return container;
        }

        private static float NextFloat(float input, int offset) {
            if (float.IsNaN(input) || float.IsPositiveInfinity(input) || float.IsNegativeInfinity(input))
                return input;

            var bytes = BitConverter.GetBytes(input);
            var bits = BitConverter.ToInt32(bytes, 0);

            if (input > 0) {
                bits += offset;
            } else if (input < 0) {
                bits -= offset;
            } else if (input == 0) {
                return (offset > 0) ? float.Epsilon : -float.Epsilon;
            }

            bytes = BitConverter.GetBytes(bits);
            return BitConverter.ToSingle(bytes, 0);
        }

        public static float NextFloatUp(float input) {
            return NextFloat(input, 1);
        }
        public static float NextFloatDown(float input) {
            return NextFloat(input, -1);
        }

        /** 見出し付きのまとまり。見出しは左寄せ、補足は小さく薄く出す。 */
        public static VisualElement Section(string title = null, string subtitle = null) {
            var section = new VisualElement();
            section.AddToClassList("spsSection");

            if (title != null || subtitle != null) {
                var header = new VisualElement();
                header.AddToClassList("spsSectionHeader");
                if (title != null) {
                    header.Add(WrappedLabel(title).AddClass("spsSectionTitle"));
                }
                if (subtitle != null) {
                    header.Add(WrappedLabel(subtitle).AddClass("spsSubtitle"));
                }
                section.Add(header);
            }

            return section;
        }

        /**
         * 開け閉めできるまとまり。開閉の状態はエディタのセッション中だけ覚えておく（key ごと）。
         * summary を渡すと、閉じているときに見出しの横へ中身の要約を出す。
         */
        public static Foldout Group(string title, string key, bool defaultOpen = false, Func<string> summary = null) {
            var stateKey = "YozoLab.SPS.Inspector.Group." + key;
            var foldout = new Foldout {
                text = title,
                value = SessionState.GetBool(stateKey, defaultOpen)
            };
            foldout.AddToClassList("spsGroup");
            foldout.RegisterValueChangedCallback(e => {
                if (e.target != foldout) return;
                SessionState.SetBool(stateKey, e.newValue);
            });

            if (summary != null) {
                var summaryLabel = new Label().AddClass("spsGroupSummary");
                summaryLabel.pickingMode = PickingMode.Ignore;
                void Refresh() {
                    string text = null;
                    try { text = summary(); } catch (Exception) { }
                    summaryLabel.text = text ?? "";
                    summaryLabel.SetVisible(!string.IsNullOrEmpty(text));
                }
                foldout.RegisterCallback<AttachToPanelEvent>(_ => {
                    var toggle = foldout.Q<Toggle>(className: Foldout.toggleUssClassName);
                    var input = toggle?.Q(className: Toggle.inputUssClassName);
                    (input ?? toggle)?.Add(summaryLabel);
                    Refresh();
                });
                foldout.schedule.Execute(Refresh).Every(1000);
            }
            return foldout;
        }

        public static T AddClass<T>(this T el, string className) where T : VisualElement {
            el.AddToClassList(className);
            return el;
        }

        private enum CalloutType { Info, Warning, Error, Status }

        /** 注意書きの箱。左端に色の帯とアイコンを付ける。 */
        private static VisualElement Callout(CalloutType type, VisualElement content) {
            var box = new VisualElement().Row();
            box.AddToClassList("spsCallout");
            box.AddToClassList("spsCallout--" + type.ToString().ToLowerInvariant());
            string icon;
            switch (type) {
                case CalloutType.Warning: icon = "console.warnicon.sml"; break;
                case CalloutType.Error: icon = "console.erroricon.sml"; break;
                case CalloutType.Status: icon = "d_Search Icon"; break;
                default: icon = "console.infoicon.sml"; break;
            }
            var image = new Image {
                image = EditorGUIUtility.IconContent(icon)?.image,
                scaleMode = ScaleMode.ScaleToFit
            };
            image.AddToClassList("spsCalloutIcon");
            box.Add(image);
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            box.Add(content);
            return box;
        }

        public static VisualElement Info(string message) {
            return Callout(CalloutType.Info, WrappedLabel(message));
        }

        public static VisualElement Info(VisualElement content) {
            return Callout(CalloutType.Info, content);
        }

        /**
         * 自動で調べた結果を定期的に出し直す箱。
         * refreshElement なら中身を丸ごと作り直す（空なら何も出さない）。
         * refreshMessage なら「状態」の箱に文字で出す（空なら隠す）。
         */
        public static VisualElement Debug(string message = "", Func<string> refreshMessage = null, Func<VisualElement> refreshElement = null, float interval = 1) {

            var loggedError = false;
            if (refreshElement != null) {
                var holder = new VisualElement();
                void Update() {
                    holder.Clear();
                    try {
                        var newContent = refreshElement();
                        if (newContent != null) {
                            holder.Add(newContent);
                        }
                    } catch (Exception e) {
                        holder.Add(DebugBox("表示の更新に失敗: " + e.Message));
                        if (!loggedError) {
                            loggedError = true;
                            UnityEngine.Debug.LogException(e);
                        }
                    }
                }
                Update();
                holder.schedule.Execute(Update).Every((long)(interval * 1000));
                return holder;
            }

            var label = WrappedLabel(message).AddClass("spsStatusText");
            var el = Callout(CalloutType.Status, label);
            if (refreshMessage != null) {
                void Update() {
                    var show = false;
                    try {
                        label.text = refreshMessage();
                        show = !string.IsNullOrWhiteSpace(label.text);
                    } catch (Exception e) {
                        label.text = $"エラー: {e.Message}";
                        show = true;
                    }
                    el.SetVisible(show);
                }

                Update();
                label.schedule.Execute(Update).Every((long)(interval * 1000));
            }

            return el;
        }

        public static VisualElement Error(string message) {
            return Callout(CalloutType.Error, WrappedLabel(message));
        }

        public static VisualElement Warn(string message) {
            return Warn(WrappedLabel(message));
        }
        
        public static VisualElement Warn(VisualElement message) {
            return Callout(CalloutType.Warning, message);
        }
        
        public static VisualElement DebugBox(string message) {
            return Callout(CalloutType.Status, WrappedLabel(message));
        }
        
        public static Type GetManagedReferenceType(SerializedProperty prop) {
            var typename = prop.managedReferenceFullTypename;
            var i = typename.IndexOf(' ');
            if (i > 0) {
                var assemblyPart = typename.Substring(0, i);
                var nsClassnamePart = typename.Substring(i);
                return Type.GetType($"{nsClassnamePart}, {assemblyPart}");
            }
            return null;
        }
        
        public static string GetManagedReferenceTypeName(SerializedProperty prop) {
            return GetManagedReferenceType(prop)?.Name;
        }

        /**
         * VRLabs Ragdoll System makes a copy of the entire armature, including VRCFury components,
         * which can result in a lot of duplicates.
         */
        public static bool IsInRagdollSystem(VFGameObject obj) {
            while (obj != null) {
                if (obj.name == "Ragdoll System") return true;
                if (obj.name == "CarbonCopy Container") return true;
                // DexClone_worldSpace/CloneContainer0?
                if (obj.name.StartsWith("CloneContainer")) return true;
                if (obj.name == "DexClone_worldSpace") return true;
                if (obj.name == "CCopy World Space") return true;
                obj = obj.parent;
            }
            return false;
        }
        
        public static void HoverHighlight(VisualElement el) {
            var oldBg = new StyleColor();
            
            el.RegisterCallback<MouseOverEvent>(e => {
                oldBg = el.style.backgroundColor;
                float FadeUp(float val) {
                    return (1 - val) * 0.1f + val;
                }
                var newBg = oldBg.keyword == StyleKeyword.Undefined
                    ? new Color(FadeUp(oldBg.value.r), FadeUp(oldBg.value.g), FadeUp(oldBg.value.b))
                    : new Color(1, 1, 1, 0.1f);
                el.style.backgroundColor = newBg;
            });
            el.RegisterCallback<MouseOutEvent>(e => {
                el.style.backgroundColor = oldBg;
            });
        }

        public static string Rev(string s) {
            var charArray = s.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }
        
        public static T GetResource<T>(string path) where T : Object {
            var resourcesPath = AssetDatabase.GUIDToAssetPath("f461551fdc2845d78eaa15f752a37b4b");
            return AssetDatabase.LoadAssetAtPath<T>($"{resourcesPath}/{path}");
        }

        public static T LoadGuid<T>(string guid) where T : Object {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path)) return null;
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
        
        private abstract class PropsReflection : ReflectionHelper {
            private static readonly Type ScriptAttributeUtility = ReflectionUtils.GetTypeFromAnyAssembly("UnityEditor.ScriptAttributeUtility");
            public delegate FieldInfo GetFieldInfoFromProperty_(SerializedProperty property, out System.Type type);
            public static readonly GetFieldInfoFromProperty_ GetFieldInfoFromProperty = ScriptAttributeUtility?.GetMatchingDelegate<GetFieldInfoFromProperty_>("GetFieldInfoFromProperty");
        }
        public static Type GetPropertyType(SerializedProperty prop) {
            if (PropsReflection.GetFieldInfoFromProperty == null) {
                throw new Exception("Failed to determine property type because ScriptAttributeUtility.GetFieldInfoFromProperty is unavailable");
            }
            PropsReflection.GetFieldInfoFromProperty(prop, out var type);
            if (type == null) {
                throw new Exception("Failed to determine property type for property: " + prop.propertyPath);
            }
            return type;
        }

        public static VisualElement CheckboxList(SerializedProperty depthActionsList, string label, string tooltip, string sectionTitle, VisualElement sectionBody = null) {
            if (sectionBody == null) sectionBody = List(depthActionsList);
            var container = new VisualElement();
            var enabledCheckbox = new Toggle();
            container.Add(BetterProp(
                null,
                label,
                tooltip: tooltip,
                fieldOverride: enabledCheckbox
            ));
            var section = Section(sectionTitle);
            section.Add(sectionBody);
            container.Add(section);

            enabledCheckbox.RegisterValueChangedCallback(e => {
                if (e.newValue) {
                    section.SetVisible(true);
                } else {
                    depthActionsList.ClearArray();
                    depthActionsList.serializedObject.ApplyModifiedProperties();
                    UpdateState();
                }
            });

            void UpdateState() {
                var show = depthActionsList.arraySize > 0;
                section.SetVisible(show);
                enabledCheckbox.SetValueWithoutNotify(show);
            }
            container.Add(OnChange(depthActionsList, UpdateState));
            UpdateState();
            return container;
        }

        public static VisualElement FilteredGameObjectProp<T>(SerializedProperty prop) where T : UnityEngine.Component {
            var output = new VisualElement();

            var visibleField = new ObjectField();
            output.Add(visibleField);
            visibleField.objectType = typeof(T);
            visibleField.RegisterValueChangedCallback(e => {
                GameObject go = null;
                if (e.newValue is T r && r != null) {
                    go = r.owner();
                }
                prop.objectReferenceValue = go;
                prop.serializedObject.ApplyModifiedProperties();
            });

            void UpdateState() {
                Object shown = null;
                var obj = prop.objectReferenceValue as GameObject;
                if (obj != null) {
                    var r = obj.GetComponent<T>();
                    shown = r;
                }
                visibleField.SetValueWithoutNotify(shown);
            }
            output.Add(OnChange(prop, UpdateState));
            UpdateState();
            return output;
        }
    }
}
