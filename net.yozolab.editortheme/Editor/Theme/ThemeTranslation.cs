// 素の Unity の色から「役割」への表。ここがテーマ非依存の土台。
//
// 以前はここが「Unity の色 → Iceberg の色」の直書きだった。テーマを増やすには
// テーマごとに 176 行を書き直す必要があり、現実的でなかったので役割を挟んだ。
// テーマは役割ごとの色だけを持てばよい（ThemeDefinition）。
//
// Dark と Light で表が別なのは、同じ素の色でも役割が違うため。たとえば #101010 は
// Dark では面だが Light では文字になる。
using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    internal static class ThemeTranslation
    {
        private static readonly Dictionary<string, ThemeRole> DarkRoles = new Dictionary<string, ThemeRole>
        {
            ["00FF00"] = ThemeRole.Success, ["0593E0"] = ThemeRole.Info, ["091141"] = ThemeRole.SurfaceDeepest,
            ["0D0D0D"] = ThemeRole.SurfaceDeepest, ["101010"] = ThemeRole.SurfaceDeepest, ["106FCD"] = ThemeRole.Accent,
            ["151515"] = ThemeRole.SurfaceDeepest, ["191919"] = ThemeRole.SurfaceDeepest, ["1A1A1A"] = ThemeRole.SurfaceDeepest,
            ["1D1D1D"] = ThemeRole.SurfaceDeepest, ["202020"] = ThemeRole.SurfaceDeepest, ["212121"] = ThemeRole.SurfaceDeepest,
            ["222222"] = ThemeRole.SurfaceDeepest, ["232323"] = ThemeRole.SurfaceDeepest, ["242424"] = ThemeRole.SurfaceDeepest,
            ["252525"] = ThemeRole.SurfaceDeepest, ["282828"] = ThemeRole.SurfaceDeepest, ["2A2A2A"] = ThemeRole.SurfaceDeepest,
            ["2C5D87"] = ThemeRole.Selection, ["2C668C"] = ThemeRole.Selection, ["303030"] = ThemeRole.Surface,
            ["313131"] = ThemeRole.SurfaceRaised, ["323232"] = ThemeRole.SurfaceRaised, ["333333"] = ThemeRole.Surface,
            ["3356DA"] = ThemeRole.Accent, ["353535"] = ThemeRole.SurfaceDeepest, ["373737"] = ThemeRole.SurfaceRaised,
            ["383838"] = ThemeRole.Surface, ["393939"] = ThemeRole.Surface, ["3A79BB"] = ThemeRole.Accent,
            ["3C3C3C"] = ThemeRole.SurfaceRaised, ["3D80DF"] = ThemeRole.Accent, ["3E3E3E"] = ThemeRole.SurfaceRaised,
            ["3E5F96"] = ThemeRole.Selection, ["3F3F3F"] = ThemeRole.SurfaceRaised, ["404040"] = ThemeRole.SurfaceRaised,
            ["424242"] = ThemeRole.SurfaceHover, ["434343"] = ThemeRole.SurfaceHover, ["454545"] = ThemeRole.SurfaceHover,
            ["464646"] = ThemeRole.SurfaceHover, ["46607C"] = ThemeRole.Selection, ["474747"] = ThemeRole.SurfaceHover,
            ["494949"] = ThemeRole.SurfaceHover, ["4C4C4C"] = ThemeRole.Control, ["4C7EFF"] = ThemeRole.Accent,
            ["4D4D4D"] = ThemeRole.SurfaceHover, ["4F657F"] = ThemeRole.Selection, ["505050"] = ThemeRole.Control,
            ["515151"] = ThemeRole.Control, ["565656"] = ThemeRole.Control, ["575757"] = ThemeRole.SurfaceHover,
            ["585858"] = ThemeRole.ControlBright, ["595959"] = ThemeRole.ControlBright, ["5E5E5E"] = ThemeRole.Control,
            ["5F5F5F"] = ThemeRole.ControlBright, ["606060"] = ThemeRole.Selection, ["636363"] = ThemeRole.ControlBright,
            ["656565"] = ThemeRole.ControlBright, ["666666"] = ThemeRole.ControlBright, ["676767"] = ThemeRole.ControlBrightest,
            ["686868"] = ThemeRole.ControlBrightest, ["6A6A6A"] = ThemeRole.ControlBrightest, ["6E6E6E"] = ThemeRole.ControlBrightest,
            ["7B7B7B"] = ThemeRole.ControlBright, ["7BAEFA"] = ThemeRole.Accent, ["81B4FF"] = ThemeRole.AccentBright,
            ["89D8FF"] = ThemeRole.InfoBright, ["999999"] = ThemeRole.TextDim, ["A6A6A6"] = ThemeRole.TextMuted,
            ["B4B4B4"] = ThemeRole.TextMuted, ["BDBDBD"] = ThemeRole.Text, ["C4C4C4"] = ThemeRole.Text,
            ["CCCCCC"] = ThemeRole.Text, ["D2D2D2"] = ThemeRole.TextBright, ["D32222"] = ThemeRole.Error,
            ["DEDEDE"] = ThemeRole.TextBright, ["E4E4E4"] = ThemeRole.TextBright, ["EAEAEA"] = ThemeRole.TextBright,
            ["EEEEEE"] = ThemeRole.TextBright, ["F0513C"] = ThemeRole.Error, ["F4BC02"] = ThemeRole.Warning,
            ["FF00FF"] = ThemeRole.Purple, ["FF7F7F"] = ThemeRole.ErrorBright, ["FFA53C"] = ThemeRole.Warning,
            ["FFAA6D"] = ThemeRole.WarningBright, ["FFFFFF"] = ThemeRole.TextBrightest,
        };

        private static readonly Dictionary<string, ThemeRole> LightRoles = new Dictionary<string, ThemeRole>
        {
            ["000000"] = ThemeRole.Text, ["0032E6"] = ThemeRole.Accent, ["003C88"] = ThemeRole.Accent,
            ["00FF00"] = ThemeRole.Success, ["018CFF"] = ThemeRole.Info, ["090909"] = ThemeRole.Text,
            ["0C6CCB"] = ThemeRole.Accent, ["101010"] = ThemeRole.Text, ["151515"] = ThemeRole.Text,
            ["161616"] = ThemeRole.Text, ["1D5087"] = ThemeRole.Accent, ["222222"] = ThemeRole.Text,
            ["282828"] = ThemeRole.Text, ["32A3E1"] = ThemeRole.Info, ["333308"] = ThemeRole.Warning,
            ["333333"] = ThemeRole.Text, ["3351E2"] = ThemeRole.Accent, ["3356DA"] = ThemeRole.Accent,
            ["356AA3"] = ThemeRole.Accent, ["3A72B0"] = ThemeRole.Selection, ["3D80DF"] = ThemeRole.Accent,
            ["3E5F96"] = ThemeRole.Accent, ["4C7EFF"] = ThemeRole.Accent, ["4E8DD3"] = ThemeRole.Info,
            ["4F4F4F"] = ThemeRole.Control, ["555555"] = ThemeRole.Control, ["565656"] = ThemeRole.Control,
            ["595959"] = ThemeRole.Control, ["5A0000"] = ThemeRole.Error, ["606060"] = ThemeRole.SurfaceDeepest,
            ["616161"] = ThemeRole.Control, ["636363"] = ThemeRole.Control, ["656565"] = ThemeRole.ControlBrightest,
            ["666666"] = ThemeRole.TextDim, ["696969"] = ThemeRole.Control, ["6B6B6B"] = ThemeRole.ControlBright,
            ["6C6C6C"] = ThemeRole.ControlBright, ["707070"] = ThemeRole.ControlBright, ["737373"] = ThemeRole.Control,
            ["7B7B7B"] = ThemeRole.ControlBright, ["7F7F7F"] = ThemeRole.ControlBright, ["808080"] = ThemeRole.TextDim,
            ["8A8A8A"] = ThemeRole.ControlBright, ["8E8E8E"] = ThemeRole.ControlBrightest, ["8F8F8F"] = ThemeRole.ControlBright,
            ["939393"] = ThemeRole.ControlBright, ["96C3FB"] = ThemeRole.Selection, ["999999"] = ThemeRole.ControlBright,
            ["9A9A9A"] = ThemeRole.ControlBright, ["9B9B9B"] = ThemeRole.ControlBright, ["A0A0A0"] = ThemeRole.ControlBright,
            ["A4A4A4"] = ThemeRole.SurfaceHover, ["A5A5A5"] = ThemeRole.SurfaceHover, ["A7A7A7"] = ThemeRole.ControlBright,
            ["A9A9A9"] = ThemeRole.ControlBright, ["AA00AA"] = ThemeRole.Purple, ["AEAEAE"] = ThemeRole.SurfaceHover,
            ["B0B0B0"] = ThemeRole.SurfaceHover, ["B0D2FC"] = ThemeRole.SurfaceHover, ["B1B1B1"] = ThemeRole.SurfaceDeepest,
            ["B3B3B3"] = ThemeRole.SurfaceDeepest, ["B6B6B6"] = ThemeRole.SurfaceDeepest, ["B73C15"] = ThemeRole.Warning,
            ["B7B7B7"] = ThemeRole.SurfaceDeepest, ["B9B9B9"] = ThemeRole.SurfaceDeepest, ["BABABA"] = ThemeRole.SurfaceDeepest,
            ["BBBBBB"] = ThemeRole.SurfaceDeepest, ["BCBCBC"] = ThemeRole.SurfaceDeepest, ["BEBEBE"] = ThemeRole.SurfaceDeepest,
            ["C1C1C1"] = ThemeRole.SurfaceRaised, ["C2C2C2"] = ThemeRole.SurfaceDeepest, ["C8C8C8"] = ThemeRole.SurfaceRaised,
            ["CACACA"] = ThemeRole.SurfaceRaised, ["CBCBCB"] = ThemeRole.SurfaceDeepest, ["CCCCCC"] = ThemeRole.SurfaceDeepest,
            ["CFCFCF"] = ThemeRole.SurfaceRaised, ["D1F7FF"] = ThemeRole.SurfaceRaised, ["D6D6D6"] = ThemeRole.Surface,
            ["DEDEDE"] = ThemeRole.SurfaceRaised, ["DFDFDF"] = ThemeRole.SurfaceRaised, ["E4E4E4"] = ThemeRole.SurfaceRaised,
            ["EBEBEB"] = ThemeRole.SurfaceRaised, ["ECECEC"] = ThemeRole.SurfaceDeepest, ["EDEDED"] = ThemeRole.Surface,
            ["EFEFEF"] = ThemeRole.Selection, ["F0513C"] = ThemeRole.Error, ["F0F0F0"] = ThemeRole.Surface,
            ["FF9999"] = ThemeRole.Error, ["FFB299"] = ThemeRole.Warning, ["FFFFFF"] = ThemeRole.TextBrightest,
        };

        /// <summary>素の色を、テーマの色へ置き換える。素の表はテーマの明暗で選ぶ。</summary>
        public static Color Translate(Color source, ThemeDefinition theme)
            => Translate(source, theme, theme?.isDark ?? true);

        /// <summary>
        /// 素の色を、テーマの色へ置き換える。
        ///
        /// <paramref name="stockDark"/> は「今 Unity が使っている素の色が暗色側か」。
        /// テーマの明暗とは別物で、混同すると暗いエディタに明色テーマを当てられなくなる。
        /// 素の色は Unity のスキンから来るので表はそちらで選び、塗る色はテーマから取る。
        /// </summary>
        public static Color Translate(Color source, ThemeDefinition theme, bool stockDark)
        {
            if (theme == null) return source;

            Dictionary<string, ThemeRole> table = stockDark ? DarkRoles : LightRoles;
            string key = ColorUtility.ToHtmlStringRGB(source);

            if (!table.TryGetValue(key, out ThemeRole role))
            {
                // 表に無い灰色は、明るさの最も近い項目の役割へ寄せる。
                // 表に無い色付きの色は意味を持っている可能性があるので触らない。
                if (!IsGrey(source)) return source;
                if (!TryNearestGrey(source, table, out role)) return source;
            }

            Color target = theme.Get(role);
            target.a = source.a;
            return target;
        }

        private static bool IsGrey(Color c)
        {
            float max = Mathf.Max(c.r, c.g, c.b);
            float min = Mathf.Min(c.r, c.g, c.b);
            return max - min <= 0.04f;
        }

        private static bool TryNearestGrey(Color c, Dictionary<string, ThemeRole> table, out ThemeRole role)
        {
            role = default;
            float luminance = (c.r + c.g + c.b) / 3f;
            float best = 0.12f; // これより遠ければ寄せない
            bool found = false;

            foreach (KeyValuePair<string, ThemeRole> pair in table)
            {
                if (!ColorUtility.TryParseHtmlString("#" + pair.Key, out Color key) || !IsGrey(key)) continue;
                float distance = Mathf.Abs((key.r + key.g + key.b) / 3f - luminance);
                if (distance >= best) continue;
                best = distance; role = pair.Value; found = true;
            }
            return found;
        }
    }
}
