using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    /// <summary>
    /// Unity のテーマが使う色から Iceberg (cocopon/iceberg.vim) の色への翻訳表。
    ///
    /// Unity 2022 のテーマシート（DefaultCommonDark_inter.uss）は生成済みで、
    /// 規則の中の var() 参照は import 時に解決され、色はリテラルで焼き込まれている。
    /// 「ボタンの背景」「ドロップダウンの hover」といった役割の区別は残っておらず、
    /// 同じ灰色を複数の役割が共有している。そのため役割ごとではなく
    /// **色の値ごと** に 1 対 1 で置き換えるしかない。表の右側が同じ値に
    /// 潰れているところは、その制約の結果であって手抜きではない。
    ///
    /// 表の左側は実際のテーマシートの色テーブル（449 色、固有 93 色）を吐き出して
    /// 作ったもの。Unity のバージョンが変わって表に無い灰色が出てきた場合は、
    /// 明るさの近い項目へ寄せる（<see cref="Translate"/>）。色付きの未知の色は触らない。
    /// アルファは元の値をそのまま引き継ぐ。
    /// </summary>
    internal static class IcebergTranslation
    {
        /// <summary>Unity Dark → Iceberg dark。キーも値も RGB の 6 桁。</summary>
        private static readonly Dictionary<string, string> DarkTable = new Dictionary<string, string>
        {
            // ── 面: 暗い順 ──────────────────────────────────────────
            ["0D0D0D"] = "0f1117", ["101010"] = "0f1117", ["151515"] = "0f1117", ["191919"] = "0f1117",
            ["1A1A1A"] = "0f1117", ["1D1D1D"] = "0f1117", ["202020"] = "0f1117", ["212121"] = "0f1117",
            ["222222"] = "0f1117", ["232323"] = "0f1117", ["242424"] = "0f1117", ["252525"] = "0f1117",
            ["282828"] = "0f1117", ["2A2A2A"] = "0f1117", ["091141"] = "0f1117",
            ["303030"] = "161821", ["333333"] = "161821", ["353535"] = "0f1117",
            ["313131"] = "1e2132", ["323232"] = "1e2132", ["373737"] = "1e2132",
            ["383838"] = "161821", ["393939"] = "161821",
            ["3C3C3C"] = "1e2132", ["3E3E3E"] = "1e2132", ["3F3F3F"] = "1e2132", ["404040"] = "1e2132",
            ["424242"] = "272c42", ["434343"] = "272c42", ["454545"] = "272c42", ["464646"] = "272c42",
            ["474747"] = "272c42", ["494949"] = "272c42", ["4D4D4D"] = "272c42", ["575757"] = "272c42",
            ["4C4C4C"] = "3d425b", ["505050"] = "3d425b", ["515151"] = "3d425b", ["565656"] = "3d425b",
            ["5E5E5E"] = "3d425b",
            ["585858"] = "444b71", ["595959"] = "444b71", ["5F5F5F"] = "444b71", ["636363"] = "444b71",
            ["656565"] = "444b71", ["666666"] = "444b71", ["7B7B7B"] = "444b71",
            ["676767"] = "5b6389", ["686868"] = "5b6389", ["6A6A6A"] = "5b6389", ["6E6E6E"] = "5b6389",
            ["606060"] = "2a3158",
            // ── 文字: 灰色の階調 ─────────────────────────────────────
            ["999999"] = "818596", ["A6A6A6"] = "9a9ca5", ["B4B4B4"] = "9a9ca5",
            ["BDBDBD"] = "c6c8d1", ["C4C4C4"] = "c6c8d1", ["CCCCCC"] = "c6c8d1",
            ["D2D2D2"] = "d2d4de", ["DEDEDE"] = "d2d4de", ["E4E4E4"] = "d2d4de", ["EAEAEA"] = "d2d4de",
            ["EEEEEE"] = "d2d4de", ["FFFFFF"] = "eff0f4",
            // ── 選択・強調 ───────────────────────────────────────────
            ["2C5D87"] = "2a3158", ["2C668C"] = "2a3158", ["46607C"] = "2a3158", ["4F657F"] = "2a3158",
            ["3E5F96"] = "2a3158",
            ["3A79BB"] = "84a0c6", ["7BAEFA"] = "84a0c6", ["4C7EFF"] = "84a0c6", ["106FCD"] = "84a0c6",
            ["3D80DF"] = "84a0c6", ["3356DA"] = "84a0c6", ["81B4FF"] = "91acd1",
            ["0593E0"] = "89b8c2", ["89D8FF"] = "95c4ce",
            // ── 意味を持つ色 ─────────────────────────────────────────
            ["D32222"] = "e27878", ["F0513C"] = "e27878", ["FF7F7F"] = "e98989",
            ["F4BC02"] = "e2a478", ["FFA53C"] = "e2a478", ["FFAA6D"] = "e9b189",
            ["FF00FF"] = "a093c7", ["00FF00"] = "b4be82",
        };

        /// <summary>Unity Light → Iceberg light。Iceberg light は明るい側の階調が薄いので、面は詰まり気味になる。</summary>
        private static readonly Dictionary<string, string> LightTable = new Dictionary<string, string>
        {
            // ── 面 ───────────────────────────────────────────────────
            ["A5A5A5"] = "c9cdd7", ["B0B0B0"] = "c9cdd7", ["B1B1B1"] = "cad0de", ["B3B3B3"] = "cad0de",
            ["B6B6B6"] = "cad0de", ["B7B7B7"] = "cad0de", ["B9B9B9"] = "cad0de", ["BABABA"] = "cad0de",
            ["BBBBBB"] = "cad0de", ["BCBCBC"] = "cad0de", ["BEBEBE"] = "cad0de", ["C2C2C2"] = "cad0de",
            ["CBCBCB"] = "cad0de", ["CCCCCC"] = "cad0de",
            ["C1C1C1"] = "dcdfe7", ["C8C8C8"] = "dcdfe7", ["CACACA"] = "dcdfe7", ["CFCFCF"] = "dcdfe7",
            ["DEDEDE"] = "dcdfe7", ["DFDFDF"] = "dcdfe7", ["E4E4E4"] = "dcdfe7", ["EBEBEB"] = "dcdfe7",
            ["D6D6D6"] = "e8e9ec", ["ECECEC"] = "cad0de", ["EDEDED"] = "e8e9ec", ["EFEFEF"] = "a7b2cd",
            ["F0F0F0"] = "e8e9ec", ["D1F7FF"] = "dcdfe7",
            // ── 線・つまみ ───────────────────────────────────────────
            ["6B6B6B"] = "9fa7bd", ["6C6C6C"] = "9fa7bd", ["707070"] = "9fa7bd", ["7B7B7B"] = "9fa7bd",
            ["7F7F7F"] = "9fa7bd", ["8A8A8A"] = "9fa7bd", ["8E8E8E"] = "8b98b6", ["8F8F8F"] = "9fa7bd",
            ["939393"] = "9fa7bd", ["999999"] = "9fa7bd", ["9A9A9A"] = "9fa7bd", ["9B9B9B"] = "9fa7bd",
            ["A0A0A0"] = "9fa7bd", ["A4A4A4"] = "c9cdd7", ["A7A7A7"] = "9fa7bd", ["A9A9A9"] = "9fa7bd",
            ["AEAEAE"] = "c9cdd7", ["808080"] = "8389a3",
            ["4F4F4F"] = "757ca3", ["616161"] = "757ca3", ["656565"] = "8b98b6", ["666666"] = "8389a3",
            ["555555"] = "757ca3", ["565656"] = "757ca3", ["595959"] = "757ca3", ["606060"] = "cad0de",
            ["636363"] = "757ca3", ["696969"] = "757ca3", ["737373"] = "757ca3",
            // ── 文字 ─────────────────────────────────────────────────
            ["000000"] = "33374c", ["090909"] = "33374c", ["101010"] = "33374c", ["151515"] = "33374c",
            ["161616"] = "33374c", ["222222"] = "33374c", ["282828"] = "33374c", ["333333"] = "33374c",
            ["FFFFFF"] = "e8e9ec",
            // ── 選択・強調 ───────────────────────────────────────────
            ["3A72B0"] = "a7b2cd", ["96C3FB"] = "a7b2cd", ["B0D2FC"] = "c9cdd7",
            ["0032E6"] = "2d539e", ["003C88"] = "2d539e", ["0C6CCB"] = "2d539e", ["1D5087"] = "2d539e",
            ["4C7EFF"] = "2d539e", ["3351E2"] = "2d539e", ["3356DA"] = "2d539e", ["356AA3"] = "2d539e",
            ["3D80DF"] = "2d539e", ["3E5F96"] = "2d539e",
            ["018CFF"] = "3f83a6", ["32A3E1"] = "3f83a6", ["4E8DD3"] = "3f83a6",
            // ── 意味を持つ色 ─────────────────────────────────────────
            ["5A0000"] = "cc517a", ["F0513C"] = "cc517a", ["FF9999"] = "cc517a",
            ["333308"] = "c57339", ["B73C15"] = "c57339", ["FFB299"] = "c57339",
            ["AA00AA"] = "7759b4", ["00FF00"] = "668e3d",
        };

        /// <summary>
        /// 色を翻訳する。表に無い灰色は、表の中で明るさの最も近い灰色の行き先へ寄せる。
        /// 表に無い色付きの色はそのまま返す（意味を持っている可能性があるため）。
        /// </summary>
        public static Color Translate(Color source, bool dark)
        {
            Dictionary<string, string> table = dark ? DarkTable : LightTable;
            string key = ColorUtility.ToHtmlStringRGB(source);

            if (!table.TryGetValue(key, out string hex))
            {
                if (!IsGrey(source)) return source;
                hex = NearestGrey(source, table);
                if (hex == null) return source;
            }

            ColorUtility.TryParseHtmlString("#" + hex, out Color target);
            target.a = source.a;
            return target;
        }

        private static bool IsGrey(Color c)
        {
            float max = Mathf.Max(c.r, c.g, c.b);
            float min = Mathf.Min(c.r, c.g, c.b);
            return max - min <= 0.04f;
        }

        private static string NearestGrey(Color c, Dictionary<string, string> table)
        {
            float luminance = (c.r + c.g + c.b) / 3f;
            string best = null;
            float bestDistance = 0.12f; // これより遠ければ寄せない

            foreach (KeyValuePair<string, string> pair in table)
            {
                if (!ColorUtility.TryParseHtmlString("#" + pair.Key, out Color key) || !IsGrey(key)) continue;
                float distance = Mathf.Abs((key.r + key.g + key.b) / 3f - luminance);
                if (distance < bestDistance) { bestDistance = distance; best = pair.Value; }
            }

            return best;
        }
    }
}
