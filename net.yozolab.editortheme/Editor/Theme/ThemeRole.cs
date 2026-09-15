// テーマの「役割」。テーマはこの役割ごとに色を 1 つ決める。
//
// Unity のテーマシートは生成済みで、規則の中の var() 参照が import 時に解決され、
// 色がリテラルで焼き込まれている。そのため「ボタンの背景」のような役割の区別は
// 残っておらず、同じ灰色を複数の役割が共有している。こちらで役割を定義し直し、
// 素の色から役割への表（ThemeTranslation）を挟むことで、テーマ側は色を
// 役割ごとに決めるだけでよくなる。
//
// 役割の値は実際の Unity のテーマシートから機械的に導いた。Iceberg の色を入れると、
// 以前の手書きの対応表（Dark 86 / Light 90 対応）と 1 つも違わない結果になる。
namespace YozoLab.EditorTheme
{
    internal enum ThemeRole
    {
        Accent,
        AccentBright,
        Control,
        ControlBright,
        ControlBrightest,
        Error,
        ErrorBright,
        Info,
        InfoBright,
        Purple,
        Selection,
        Success,
        Surface,
        SurfaceDeepest,
        SurfaceHover,
        SurfaceRaised,
        Text,
        TextBright,
        TextBrightest,
        TextDim,
        TextMuted,
        Warning,
        WarningBright,
        ControlSurface,
        ControlSurfaceSelected,
        TextFaint,
    }
}
