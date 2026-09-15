// IMGUI の色の「出所」を塗り替える。
//
// エディタの IMGUI スタイルは USS から作られた StyleCatalog を持ち、その中の
// buffers.colors という Color[] 1 本に、あらゆるブロックの色が添字で解決される。
//
//     StyleBlock.GetColor(key) -> catalog.buffers.colors[bufferIndex]
//
// そして GUIStyle.onDraw に入っている StylePainter.DrawStyle が、こう分岐する。
//
//     if (... || gs.normal.background != null) return false;   // テクスチャがあれば Unity に返す
//     DrawBlock(gs, block, position, content, states);          // 無ければカタログから塗る
//
// つまり背景テクスチャを持たないスタイル（ボタン・入力欄・ドロップダウン・見出しなど）は、
// テクスチャではなくこのカタログの色で塗られている。スキンのテクスチャをいくら差し replace ても
// 届かなかったのはこれが理由。出所はここ。
//
// 配列を直接書き換える。カタログの再構築は起こさない（起こすと素の色を読み直してしまう）。
// 元の値は控えて、無効化で戻す。ドメインリロードでは素に戻る。
using System;
using System.Reflection;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    internal static class StyleCatalogRecolorer
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                         | BindingFlags.Static | BindingFlags.Instance;

        private static Color[] live;
        private static Color[] original;
        private static string lastError;

        /// <summary>塗り替えている色数。診断用。</summary>
        public static int PatchedCount => original?.Length ?? 0;

        /// <summary>取れなかったときの理由。診断用。</summary>
        public static string LastError => lastError;

        public static bool Apply(bool dark)
        {
            Restore();

            Color[] colors = GetColorBuffer();
            if (colors == null || colors.Length == 0) return false;

            original = (Color[])colors.Clone();
            live = colors;

            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = IcebergTranslation.Translate(colors[i], dark);
            }
            return true;
        }

        public static void Restore()
        {
            if (live != null && original != null)
            {
                try { Array.Copy(original, live, Math.Min(live.Length, original.Length)); }
                catch (Exception) { }
            }
            live = null;
            original = null;
        }

        /// <summary>
        /// 塗り替え後の色から、塗り替え前の色を引く。
        ///
        /// カタログの色を型初期化時に static へ焼き込む型がある。塗り替えたあとに
        /// 初期化されると、塗った色を「元の色」として抱え込んでしまい、こちらには
        /// 控えが無いので無効化しても戻せない（Hierarchy の可視性列で実際に起きた）。
        ///
        /// 型初期化子を先に走らせて素の色を抱えさせる案は採れない。GUI の文脈外で
        /// 走らせると EditorStyles を参照する型が NullReferenceException を投げ、
        /// .NET はその失敗を記憶するため、以後その型は永久に使えなくなる
        /// （PropertyEditor+Styles がこれでインスペクタごと死んだ）。
        ///
        /// 代わりに、塗り替え前後の配列を突き合わせて原本を引く。型には触らない。
        /// </summary>
        public static bool TryFindOriginal(Color patched, out Color stock)
        {
            stock = patched;
            if (live == null || original == null) return false;

            int n = Math.Min(live.Length, original.Length);
            for (int i = 0; i < n; i++)
            {
                if (live[i] != patched) continue;
                if (original[i] == patched) return false; // 塗り替えで変わらなかった色
                stock = original[i];
                return true;
            }
            return false;
        }

        /// <summary>
        /// EditorResources.styleCatalog.buffers.colors の実体を取る。
        /// buffers は struct を返すプロパティだが、colors は配列なので参照が取れる。
        /// styleCatalog の getter は s_StyleCatalog が null のときだけ組み立て直すので、
        /// 読むだけなら塗り替えを失わない。
        /// </summary>
        private static Color[] GetColorBuffer()
        {
            try
            {
                Type resources = typeof(UnityEditor.Editor).Assembly
                    .GetType("UnityEditor.Experimental.EditorResources");
                if (resources == null) { lastError = "EditorResources が見つからない"; return null; }

                object catalog = resources.GetProperty("styleCatalog", Any)?.GetValue(null);
                if (catalog == null) { lastError = "styleCatalog が取れない"; return null; }

                object buffers = catalog.GetType().GetProperty("buffers", Any)?.GetValue(catalog);
                if (buffers == null) { lastError = "buffers が取れない"; return null; }

                var colors = buffers.GetType().GetField("colors", Any)?.GetValue(buffers) as Color[];
                if (colors == null) { lastError = "colors が取れない"; return null; }

                lastError = null;
                return colors;
            }
            catch (Exception e)
            {
                lastError = $"{e.GetType().Name}: {e.Message}";
                return null;
            }
        }
    }
}
