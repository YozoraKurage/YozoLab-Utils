using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YozoLab.SPS.Feature.Base;
using YozoLab.SPS.Inspector;
using YozoLab.SPS.Model.StateAction;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Actions {
    [FeatureTitle("Poiyomi UV タイル")]
    internal class PoiyomiUVTileActionBuilder : ActionBuilder<PoiyomiUVTileAction> {

        public VFClip Build(PoiyomiUVTileAction model) {
            return MakeClip(model, 0);
        }
        public VFClip BuildOff(PoiyomiUVTileAction model) {
            return MakeClip(model, 1);
        }

        private VFClip MakeClip(PoiyomiUVTileAction model, int value) {
            var clip = NewClip();
            var renderer = model.renderer;
            if (renderer == null) return clip;
            if (model.row > 3 || model.row < 0 || model.column > 3 || model.column < 0) {
                throw new ArgumentException("Poiyomi UV Tiles are ranges between 0-3, check if slots are within these ranges.");
            }
            var matProp = model.dissolve ? "_UVTileDissolveAlpha_Row" : "_UDIMDiscardRow";
            matProp += $"{model.row}_{(model.column)}";
            if (model.renamedMaterial != "")
                matProp += $"_{model.renamedMaterial}";
            var propertyName = $"material.{matProp}";
            clip.SetCurve(renderer, propertyName, value);
            return clip;
        }

        [FeatureEditor]
        public static VisualElement Editor(SerializedProperty prop) {
            var content = new VisualElement();

            content.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("renderer"), "レンダラー"));
            content.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("row"), "行 (0〜3)"));
            content.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("column"), "列 (0〜3)"));

            var adv = new Foldout {
                text = "UV タイルの詳細設定",
                value = false
            };
            adv.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("dissolve"), "UV Tile Dissolve を使う"));
            adv.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("renamedMaterial"), "リネーム接尾辞", tooltip: "Poiyomi のプロパティリネーム（Rename）を使っている場合のマテリアル側の接尾辞"));
            content.Add(adv);
            return content;
        }
    }
}
