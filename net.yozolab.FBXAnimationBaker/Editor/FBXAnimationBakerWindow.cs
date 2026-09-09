using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// FBX と Humanoid AnimationClip を指定すると、そのクリップを Transform アニメーションとして
    /// ベイクし、モデルと一緒に同梱した FBX を書き出すエディタ拡張。
    ///
    /// このファイルはウィンドウのライフサイクル・設定の読み書き・多言語化のみを担当する。
    /// 機能ごとの実装は以下の partial に分割している:
    ///   - FBXAnimationBakerWindow.GUI.cs    … OnGUI / 各種 GUI 描画
    ///   - FBXAnimationBakerWindow.Baking.cs … サンプリングと Transform カーブ生成 / Execute パイプライン
    ///   - FBXAnimationBakerWindow.Export.cs … Unity FBX Exporter へのリフレクション橋渡し
    ///   - FBXAnimationBakerWindow.Template.cs … エントリ設定のコピー / ペースト
    /// </summary>
    public partial class FBXAnimationBakerWindow : EditorWindow
    {
        protected FBXAnimationBakerSettings settings;
        protected SerializedObject serializedSettings;
        protected SerializedProperty bakeEntriesProp;

        private Vector2 entryListScrollPosition;
        private Vector2 entryDetailScrollPosition;
        private int selectedEntryIndex = -1;
        private string entrySearchText = string.Empty;

        /// <summary>一括操作(フォルダ移動 / テンプレート貼り付け)の対象。空なら選択中エントリ 1 つ。</summary>
        private readonly HashSet<int> checkedEntryIndices = new HashSet<int>();

        /// <summary>エントリ設定のコピー / ペースト用テンプレート(ドメイン内で 1 個)。</summary>
        protected static EntryDetailTemplate entryTemplate;

        [MenuItem("YozoLab/FBX Animation Baker")]
        public static void ShowWindow()
        {
            GetWindow<FBXAnimationBakerWindow>("FBX Animation Baker");
        }

        private void OnEnable()
        {
            LoadOrCreateSettings();
        }

        private void OnDisable()
        {
            PersistSettings();
        }

        private void OnLostFocus()
        {
            PersistSettings();
        }

        private void LoadOrCreateSettings()
        {
            settings = FBXAnimationBakerSettings.instance;

            MigrateGlobalOutputIfNeeded();

            serializedSettings = new SerializedObject(settings);
            bakeEntriesProp = serializedSettings.FindProperty("bakeEntries");
        }

        /// <summary>
        /// 全エントリ共通だった Output Directory を、フォルダごとの Output Directory へ移す。
        ///
        /// 以前は全エントリが共通の 1 つを使っていたので、移行では
        ///   - 出力先が未設定のフォルダすべてに旧共通値を入れる(今までと同じ挙動になる)
        ///   - フォルダに属さないエントリは行き場が無くなるので、専用フォルダを作って収容する
        /// の 2 つをやる。一度きり。
        /// </summary>
        private void MigrateGlobalOutputIfNeeded()
        {
            if (settings == null || settings.perFolderDirectoriesMigrated)
            {
                return;
            }

            DefaultAsset legacyOutput = settings.outputDirectory;
            if (legacyOutput == null)
            {
                // 移行するものが無い(新規プロジェクト)。印だけ付けて終わり。
                settings.perFolderDirectoriesMigrated = true;
                settings.SaveSettings();
                return;
            }

            settings.bakeFolders ??= new List<BakeFolderState>();
            settings.bakeEntries ??= new List<AnimationBakeEntry>();

            foreach (BakeFolderState folder in settings.bakeFolders)
            {
                if (folder != null && folder.outputDirectory == null)
                {
                    folder.outputDirectory = legacyOutput;
                }
            }

            // フォルダ無しエントリの受け皿。Execute はフォルダの出力先を既定値に
            // するようになったため、ここへ入れておかないと出力先を失う。
            bool hasFolderlessEntry = settings.bakeEntries
                .Exists(entry => entry != null && string.IsNullOrWhiteSpace(entry.folder));
            if (hasFolderlessEntry)
            {
                string name = MakeUniqueFolderName("Default");
                settings.bakeFolders.Add(new BakeFolderState
                {
                    name = name,
                    outputDirectory = legacyOutput,
                });

                int moved = 0;
                foreach (AnimationBakeEntry entry in settings.bakeEntries)
                {
                    if (entry == null || !string.IsNullOrWhiteSpace(entry.folder)) continue;
                    entry.folder = name;
                    moved++;
                }
                Debug.Log($"{LogPrefix} Moved {moved} folderless entry/entries into the new folder \"{name}\".");
            }

            settings.outputDirectory = null;
            settings.perFolderDirectoriesMigrated = true;
            settings.SaveSettings();

            Debug.Log($"{LogPrefix} Migrated the shared Output Directory to per-folder Output Directory.");
        }

        private string MakeUniqueFolderName(string desired)
        {
            bool Taken(string candidate) => settings.bakeFolders.Exists(
                f => f != null && string.Equals(f.name?.Trim(), candidate, StringComparison.OrdinalIgnoreCase));

            if (!Taken(desired)) return desired;
            for (int i = 2; ; i++)
            {
                string candidate = $"{desired} {i}";
                if (!Taken(candidate)) return candidate;
            }
        }

        /// <summary>編集中の内容を ProjectSettings/ のファイルへ確定保存する。</summary>
        protected void PersistSettings()
        {
            if (settings == null)
            {
                return;
            }

            serializedSettings?.ApplyModifiedProperties();
            settings.SaveSettings();
        }

        protected static class L10n
        {
            private const string PrefKey = "FBXAnimBaker_Language";

            public static bool IsEnglish
            {
                get => EditorPrefs.GetBool(PrefKey, false);
                set => EditorPrefs.SetBool(PrefKey, value);
            }

            public static string T(string jp, string en) => IsEnglish ? en : jp;
        }
    }
}
