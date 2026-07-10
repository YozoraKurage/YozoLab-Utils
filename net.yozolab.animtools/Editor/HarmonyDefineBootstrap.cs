using System;
using System.Collections.Generic;
using UnityEditor;

namespace YozoLab.AnimTools
{
    /// <summary>
    /// Harmony（0Harmony.dll）の有無を自動検出し、スクリプティング定義シンボル
    /// "YOZOLAB_ANIMTOOLS_HARMONY" を自動で付け外しするブートストラップ。
    ///
    /// これにより、利用者が手動でシンボルを設定しなくても、
    ///   ・Harmony がある環境 → シンボルが付き、前フレーム自動キー機能が有効化
    ///   ・Harmony が無い環境 → シンボルが付かず、依存ファイルは空コンパイル（安全）
    /// となる。
    ///
    /// このファイル自体は Harmony を参照せず（リフレクションで探すだけ）常にコンパイルできる。
    /// </summary>
    [InitializeOnLoad]
    internal static class HarmonyDefineBootstrap
    {
        private const string Symbol = "YOZOLAB_ANIMTOOLS_HARMONY";

        static HarmonyDefineBootstrap()
        {
            // ドメインリロードのたびに（＝コンパイル後・プラットフォーム切替後などに）同期する。
            try
            {
                SyncDefine(HarmonyIsAvailable());
            }
            catch
            {
                // シンボル操作の失敗で他機能を巻き込まないよう握りつぶす。
            }
        }

        /// <summary>読み込み済みアセンブリから HarmonyLib.Harmony 型を探す。</summary>
        private static bool HarmonyIsAvailable()
        {
            foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.GetType("HarmonyLib.Harmony", false) != null) return true;
                }
                catch
                {
                    // 一部アセンブリは型走査で例外を投げ得るのでスキップ。
                }
            }
            return false;
        }

        /// <summary>現在選択中のビルドターゲットグループのシンボルを、必要なときだけ更新する。</summary>
        private static void SyncDefine(bool shouldBeDefined)
        {
            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group == BuildTargetGroup.Unknown) return;

#pragma warning disable 618 // グループ版 API（新 API 非対応の旧 Unity でも動くよう維持）
            string raw = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#pragma warning restore 618

            var set = new List<string>();
            bool has = false;
            foreach (string s in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string sym = s.Trim();
                if (sym.Length == 0) continue;
                if (sym == Symbol) { has = true; continue; } // 一旦除外して末尾で整える
                set.Add(sym);
            }

            if (shouldBeDefined == has) return; // 変更不要（無駄な再コンパイルを避ける）
            if (shouldBeDefined) set.Add(Symbol);

#pragma warning disable 618
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", set));
#pragma warning restore 618
        }
    }
}
