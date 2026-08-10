using UnityEngine;
using UnityEditor;

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
    /// </summary>
    public partial class FBXAnimationBakerWindow : EditorWindow
    {
        protected FBXAnimationBakerSettings settings;
        protected SerializedObject serializedSettings;
        protected SerializedProperty outputDirectoryProp;
        protected SerializedProperty bakeEntriesProp;

        private Vector2 entryListScrollPosition;
        private Vector2 entryDetailScrollPosition;
        private int selectedEntryIndex = -1;
        private string entrySearchText = string.Empty;

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

            serializedSettings = new SerializedObject(settings);
            outputDirectoryProp = serializedSettings.FindProperty("outputDirectory");
            bakeEntriesProp = serializedSettings.FindProperty("bakeEntries");
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
