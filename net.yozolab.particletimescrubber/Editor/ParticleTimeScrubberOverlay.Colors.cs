using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace YozoLab.ParticleTimeScrubber
{
    /// <summary>
    /// 「色変更」パネル。対象エフェクトに紐付く色設定を一覧し、その場で編集する。
    ///
    /// 対象にするのは、各 ParticleSystem の Start Color / Color over Lifetime と、
    /// レンダラーが使うマテリアルの Color 型プロパティ。マテリアルはシーンの
    /// 一部ではなくアセット本体を書き換える点に注意(パネル内にも注記を出す)。
    /// </summary>
    internal sealed partial class ParticleTimeScrubberOverlay
    {
        private const float ColorPanelMaxHeight = 260f;

        private void DrawColorSection(ParticleSystem root)
        {
            if (!showColors) return;

            EditorGUILayout.Space(2f);

            using (var scroll = new EditorGUILayout.ScrollViewScope(
                       colorScroll, GUILayout.MaxHeight(ColorPanelMaxHeight)))
            {
                colorScroll = scroll.scrollPosition;

                EditorGUI.BeginChangeCheck();

                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    DrawParticleColors(ps);
                }
                DrawMaterialColors(root);

                if (EditorGUI.EndChangeCheck())
                {
                    // 再生位置を保ったまま、変更後の見た目へ揃え直す。
                    ParticleScrubController.RequestResync();
                }
            }
        }

        // ---------------------------------------------------------------
        // ParticleSystem 側の色
        // ---------------------------------------------------------------

        private static void DrawParticleColors(ParticleSystem ps)
        {
            GUILayout.Label(ps.name, EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                ParticleSystem.MainModule main = ps.main;
                if (DrawMinMaxGradient(L10n.T("開始色", "Start Color"),
                        main.startColor, ps, out ParticleSystem.MinMaxGradient newStart))
                {
                    main.startColor = newStart;
                }

                ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
                if (colorOverLifetime.enabled
                    && DrawMinMaxGradient(L10n.T("寿命で変化", "Over Lifetime"),
                        colorOverLifetime.color, ps, out ParticleSystem.MinMaxGradient newOverLifetime))
                {
                    colorOverLifetime.color = newOverLifetime;
                }
            }
        }

        /// <summary>
        /// MinMaxGradient を、今のモードのまま編集できるフィールドとして描く。
        /// モードの切り替え自体は Inspector に任せる(このパネルは値だけ触る)。
        /// 変更があったときだけ true を返し、result に新しい値を入れる。
        /// </summary>
        private static bool DrawMinMaxGradient(
            string label, ParticleSystem.MinMaxGradient value, Object undoTarget,
            out ParticleSystem.MinMaxGradient result)
        {
            EditorGUI.BeginChangeCheck();

            Color color = default, colorMin = default, colorMax = default;
            Gradient gradient = null, gradientMin = null, gradientMax = null;

            switch (value.mode)
            {
                case ParticleSystemGradientMode.Color:
                    color = EditorGUILayout.ColorField(label, value.color);
                    break;

                case ParticleSystemGradientMode.TwoColors:
                    colorMin = EditorGUILayout.ColorField(label + " Min", value.colorMin);
                    colorMax = EditorGUILayout.ColorField(label + " Max", value.colorMax);
                    break;

                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    gradient = EditorGUILayout.GradientField(label, value.gradient);
                    break;

                case ParticleSystemGradientMode.TwoGradients:
                    gradientMin = EditorGUILayout.GradientField(label + " Min", value.gradientMin);
                    gradientMax = EditorGUILayout.GradientField(label + " Max", value.gradientMax);
                    break;
            }

            result = value;
            if (!EditorGUI.EndChangeCheck()) return false;

            Undo.RecordObject(undoTarget, "Particle Color");
            switch (value.mode)
            {
                case ParticleSystemGradientMode.Color:
                    result.color = color;
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    result.colorMin = colorMin;
                    result.colorMax = colorMax;
                    break;
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    result.gradient = gradient;
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    result.gradientMin = gradientMin;
                    result.gradientMax = gradientMax;
                    break;
            }
            return true;
        }

        // ---------------------------------------------------------------
        // マテリアル側の色
        // ---------------------------------------------------------------

        private static void DrawMaterialColors(ParticleSystem root)
        {
            List<Material> materials = CollectMaterials(root);
            if (materials.Count == 0) return;

            GUILayout.Label(L10n.T("マテリアル", "Materials"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                L10n.T("マテリアルはアセット本体を書き換えます(同じマテリアルを使う他のオブジェクトにも影響)。",
                       "Edits change the material asset itself (affects everything using it)."),
                MessageType.None);

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (Material material in materials)
                {
                    DrawOneMaterial(material);
                }
            }
        }

        private static List<Material> CollectMaterials(ParticleSystem root)
        {
            var result = new List<Material>();
            foreach (ParticleSystemRenderer renderer
                     in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && !result.Contains(material)) result.Add(material);
                }

                Material trail = renderer.trailMaterial;
                if (trail != null && !result.Contains(trail)) result.Add(trail);
            }
            return result;
        }

        private static void DrawOneMaterial(Material material)
        {
            Shader shader = material.shader;
            if (shader == null) return;

            bool labelDrawn = false;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Color) continue;

                string propertyName = shader.GetPropertyName(i);
                bool hdr = (shader.GetPropertyFlags(i) & ShaderPropertyFlags.HDR) != 0;

                if (!labelDrawn)
                {
                    GUILayout.Label(material.name, EditorStyles.miniBoldLabel);
                    labelDrawn = true;
                }

                EditorGUI.BeginChangeCheck();
                Color color = EditorGUILayout.ColorField(
                    new GUIContent(shader.GetPropertyDescription(i)),
                    material.GetColor(propertyName),
                    showEyedropper: true, showAlpha: true, hdr: hdr);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(material, "Material Color");
                    material.SetColor(propertyName, color);
                }
            }
        }
    }
}
