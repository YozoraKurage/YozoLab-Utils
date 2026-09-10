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

        /// <summary>
        /// リストの選択。<see cref="selectedEntryIndex"/> はそのうち「今おもてに出ている」1 つ。
        /// フォルダ移動もテンプレート貼り付けも、この選択がそのまま対象になる。
        /// </summary>
        private readonly HashSet<int> selectedEntryIndices = new HashSet<int>();

        /// <summary>エントリ 1 行の高さ。</summary>
        private const float EntryRowHeight = 20f;

        /// <summary>ドラッグ中のエントリ番号を運ぶ鍵。</summary>
        private const string EntryDragKey = "YozoLab.FBXAnimationBaker.Entries";

        // ドラッグの開始判定。押した位置から少し動くまでは掴んだことにしない。
        private Vector2 entryDragStart;
        private int entryDragCandidate = -1;

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
            MigrateMotionsIfNeeded();

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

        /// <summary>
        /// 分かれていた Humanoid Clips / BVH File を motions へまとめる。
        ///
        /// 並び順は「クリップ群 → BVH」。以前は BVH が指定されていればクリップを
        /// 無視していたが、移行では捨てずに両方入れる。捨てると設定していたはずの
        /// ものが黙って消えるので、要らなければ外してもらう方がまだ気付ける。
        /// </summary>
        private void MigrateMotionsIfNeeded()
        {
            if (settings == null || settings.motionsMigrated)
            {
                return;
            }

            settings.bakeEntries ??= new List<AnimationBakeEntry>();

            int moved = 0;
            foreach (AnimationBakeEntry entry in settings.bakeEntries)
            {
                if (entry == null) continue;

                entry.motions ??= new List<UnityEngine.Object>();

                if (entry.clips != null)
                {
                    foreach (AnimationClip clip in entry.clips)
                    {
                        if (clip != null && !entry.motions.Contains(clip)) { entry.motions.Add(clip); moved++; }
                    }
                    entry.clips.Clear();
                }

                if (entry.bvhFile != null)
                {
                    if (!entry.motions.Contains(entry.bvhFile)) { entry.motions.Add(entry.bvhFile); moved++; }
                    entry.bvhFile = null;
                }
            }

            settings.motionsMigrated = true;
            settings.SaveSettings();

            if (moved > 0)
            {
                Debug.Log($"{LogPrefix} Merged {moved} clip/BVH reference(s) into the unified Motions list.");
            }
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
