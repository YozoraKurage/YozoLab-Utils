// 素の色をテーマの色へ翻訳する入口。
//
// 以前はここに「Unity の色 → Iceberg の色」を直書きした表（Dark 86 / Light 90 対応）が
// あった。テーマを増やすにはテーマごとに全部書き直す必要があり、現実的でなかったので、
// 表は「Unity の色 → 役割」へ移し（ThemeTranslation）、テーマは役割ごとの色だけを
// 持つようにした（ThemeDefinition）。呼び出し側はここを通すだけでよい。
using UnityEngine;

namespace YozoLab.EditorTheme
{
    internal static class IcebergTranslation
    {
        /// <summary>今選ばれているテーマで翻訳する。</summary>
        public static Color Translate(Color source, bool dark)
            => ThemeTranslation.Translate(source, ThemeCatalog.Resolve(dark));
    }
}
