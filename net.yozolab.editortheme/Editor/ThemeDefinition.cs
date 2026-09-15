// テーマ 1 つ分の定義。役割ごとの色を持つだけの入れ物。
//
// JSON で読み書きできるようにしてある。テーマを増やすのに C# を書く必要はなく、
// 色を並べた .json を 1 つ置けばよい。書き方は Editor/theme-authoring.md。
using System;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    [Serializable]
    internal sealed class ThemeDefinition
    {
        public string name = "Untitled";
        public bool isDark = true;

        public string accent;
        public string accentBright;
        public string control;
        public string controlBright;
        public string controlBrightest;
        public string error;
        public string errorBright;
        public string info;
        public string infoBright;
        public string purple;
        public string selection;
        public string success;
        public string surface;
        public string surfaceDeepest;
        public string surfaceHover;
        public string surfaceRaised;
        public string text;
        public string textBright;
        public string textBrightest;
        public string textDim;
        public string textMuted;
        public string warning;
        public string warningBright;
        public string controlSurface;
        public string controlSurfaceSelected;
        public string textFaint;

        /// <summary>役割の色を引く。空欄や壊れた値はマゼンタで返す（気付けるように）。</summary>
        public Color Get(ThemeRole role)
        {
            switch (role)
            {
                case ThemeRole.Accent: return Parse(accent);
                case ThemeRole.AccentBright: return Parse(accentBright);
                case ThemeRole.Control: return Parse(control);
                case ThemeRole.ControlBright: return Parse(controlBright);
                case ThemeRole.ControlBrightest: return Parse(controlBrightest);
                case ThemeRole.Error: return Parse(error);
                case ThemeRole.ErrorBright: return Parse(errorBright);
                case ThemeRole.Info: return Parse(info);
                case ThemeRole.InfoBright: return Parse(infoBright);
                case ThemeRole.Purple: return Parse(purple);
                case ThemeRole.Selection: return Parse(selection);
                case ThemeRole.Success: return Parse(success);
                case ThemeRole.Surface: return Parse(surface);
                case ThemeRole.SurfaceDeepest: return Parse(surfaceDeepest);
                case ThemeRole.SurfaceHover: return Parse(surfaceHover);
                case ThemeRole.SurfaceRaised: return Parse(surfaceRaised);
                case ThemeRole.Text: return Parse(text);
                case ThemeRole.TextBright: return Parse(textBright);
                case ThemeRole.TextBrightest: return Parse(textBrightest);
                case ThemeRole.TextDim: return Parse(textDim);
                case ThemeRole.TextMuted: return Parse(textMuted);
                case ThemeRole.Warning: return Parse(warning);
                case ThemeRole.WarningBright: return Parse(warningBright);
                case ThemeRole.ControlSurface: return Parse(controlSurface);
                case ThemeRole.ControlSurfaceSelected: return Parse(controlSurfaceSelected);
                case ThemeRole.TextFaint: return Parse(textFaint);
            }
            return Color.magenta;
        }

        /// <summary>役割の色を書き換える。設定画面の色編集から使う。</summary>
        public void Set(ThemeRole role, Color value)
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(value);
            switch (role)
            {
                case ThemeRole.Accent: accent = hex; return;
                case ThemeRole.AccentBright: accentBright = hex; return;
                case ThemeRole.Control: control = hex; return;
                case ThemeRole.ControlBright: controlBright = hex; return;
                case ThemeRole.ControlBrightest: controlBrightest = hex; return;
                case ThemeRole.Error: error = hex; return;
                case ThemeRole.ErrorBright: errorBright = hex; return;
                case ThemeRole.Info: info = hex; return;
                case ThemeRole.InfoBright: infoBright = hex; return;
                case ThemeRole.Purple: purple = hex; return;
                case ThemeRole.Selection: selection = hex; return;
                case ThemeRole.Success: success = hex; return;
                case ThemeRole.Surface: surface = hex; return;
                case ThemeRole.SurfaceDeepest: surfaceDeepest = hex; return;
                case ThemeRole.SurfaceHover: surfaceHover = hex; return;
                case ThemeRole.SurfaceRaised: surfaceRaised = hex; return;
                case ThemeRole.Text: text = hex; return;
                case ThemeRole.TextBright: textBright = hex; return;
                case ThemeRole.TextBrightest: textBrightest = hex; return;
                case ThemeRole.TextDim: textDim = hex; return;
                case ThemeRole.TextMuted: textMuted = hex; return;
                case ThemeRole.Warning: warning = hex; return;
                case ThemeRole.WarningBright: warningBright = hex; return;
                case ThemeRole.ControlSurface: controlSurface = hex; return;
                case ThemeRole.ControlSurfaceSelected: controlSurfaceSelected = hex; return;
                case ThemeRole.TextFaint: textFaint = hex; return;
            }
        }

        private static Color Parse(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.magenta;
            if (hex[0] != '#') hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static ThemeDefinition FromJson(string json)
        {
            try { return JsonUtility.FromJson<ThemeDefinition>(json); }
            catch (Exception) { return null; }
        }

        public ThemeDefinition Clone() => FromJson(ToJson());

        /// <summary>IMGUI 側が使う 11 色へ落とす。</summary>
        public IcebergPalette.Palette ToPalette() => new IcebergPalette.Palette
        {
            Background = Get(ThemeRole.Surface),
            BackgroundDark = Get(ThemeRole.SurfaceDeepest),
            Line = Get(ThemeRole.SurfaceRaised),
            Visual = Get(ThemeRole.SurfaceHover),
            Selection = Get(ThemeRole.Selection),
            Menu = Get(ThemeRole.ControlSurface),
            MenuSelected = Get(ThemeRole.ControlSurfaceSelected),
            Foreground = Get(ThemeRole.Text),
            ForegroundBright = Get(ThemeRole.TextBright),
            ForegroundDim = Get(ThemeRole.TextFaint),
            Blue = Get(ThemeRole.Accent),
            IsDark = isDark,
        };
    }
}
