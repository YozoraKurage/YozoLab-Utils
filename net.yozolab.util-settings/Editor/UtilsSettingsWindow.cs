using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YozoLab.UtilSettings
{
    /// <summary>
    /// このリポジトリのユーティリティを一覧し、二つの軸で切り替える窓。
    ///
    ///   コンパイル … asmdef を書き換えてアセンブリごと通す/通さないを決める。
    ///                Apply を押すまで反映されず、押すと再コンパイルが走る。
    ///                切ったパッケージは Harmony への参照ごと消える。
    ///   有効        … コンパイル済みのものを実行時に ON/OFF する。即時。
    ///
    /// 対象パッケージの型は一切参照していない（<see cref="RuntimeToggle"/> 参照）。
    /// コンパイルを切られた相手を参照すると、この窓自身が巻き添えで壊れるため。
    /// </summary>
    internal sealed class UtilsSettingsWindow : EditorWindow
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private HashSet<string> saved;      // 設定ファイルの内容
        private HashSet<string> pending;    // 画面上の編集中の状態
        private Vector2 scroll;

        [MenuItem("YozoLab/Utils Settings", false, 0)]
        private static void Open()
        {
            var window = GetWindow<UtilsSettingsWindow>();
            window.titleContent = new GUIContent("YozoLab Utils");
            window.minSize = new Vector2(420f, 320f);
            window.Show();
        }

        private void OnEnable() => Reload();

        private void Reload()
        {
            saved = UtilsCompileState.LoadEnabledIds();
            pending = new HashSet<string>(saved);
        }

        private bool HasPendingChange => pending != null && saved != null && !pending.SetEquals(saved);

        private void OnGUI()
        {
            if (pending == null) Reload();

            EditorGUILayout.HelpBox(
                "「コンパイル」を外したパッケージはアセンブリごとビルドされなくなります"
                + "（Harmony への参照も含めて消えます）。Apply を押すと再コンパイルが走ります。\n"
                + "「有効」はコンパイル済みのものを実行時に切り替えるもので、即座に反映されます。",
                MessageType.None);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (UtilPackage package in UtilsCatalog.Packages)
                DrawPackage(package);
            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void DrawPackage(UtilPackage package)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool want = pending.Contains(package.Id);
                    bool now = EditorGUILayout.ToggleLeft(
                        new GUIContent(package.DisplayName, package.Description), want, EditorStyles.boldLabel);
                    if (now != want)
                    {
                        if (now) pending.Add(package.Id);
                        else pending.Remove(package.Id);
                    }

                    GUILayout.FlexibleSpace();

                    bool compiled = UtilsCompileState.IsCompiledIn(package);
                    if (!string.IsNullOrEmpty(package.OpenMenuPath))
                    {
                        using (new EditorGUI.DisabledScope(!compiled))
                        {
                            if (GUILayout.Button("開く", GUILayout.Width(48f)))
                                EditorApplication.ExecuteMenuItem(package.OpenMenuPath);
                        }
                    }
                }

                EditorGUILayout.LabelField(package.Description, EditorStyles.miniLabel);

                if (pending.Contains(package.Id) != saved.Contains(package.Id))
                {
                    EditorGUILayout.LabelField(
                        saved.Contains(package.Id) ? "→ Apply で無効になります" : "→ Apply で有効になります",
                        EditorStyles.miniBoldLabel);
                }

                DrawRuntimeToggles(package);
            }
        }

        private void DrawRuntimeToggles(UtilPackage package)
        {
            if (package.Toggles == null || package.Toggles.Length == 0) return;

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (RuntimeToggle toggle in package.Toggles)
                {
                    Type type = FindType(toggle.TypeName);
                    if (type == null)
                    {
                        // コンパイルされていない、あるいは名前が変わった。触れるものが無い。
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.ToggleLeft(new GUIContent(toggle.Label + "（コンパイルされていません）"), false);
                        continue;
                    }

                    bool? state = ReadEnabled(type);
                    if (state == null)
                    {
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.ToggleLeft(new GUIContent(toggle.Label + "（状態を読めません）"), false);
                        continue;
                    }

                    bool now = EditorGUILayout.ToggleLeft(new GUIContent(toggle.Label, toggle.Tooltip), state.Value);
                    if (now != state.Value) WriteEnabled(type, now);
                }
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!HasPendingChange))
                {
                    if (GUILayout.Button("Revert", GUILayout.Width(80f)))
                    {
                        pending = new HashSet<string>(saved);
                        GUI.FocusControl(null);
                    }

                    if (GUILayout.Button("Apply", GUILayout.Width(80f)))
                        Apply();
                }
            }
        }

        private void Apply()
        {
            UtilsCompileState.SaveEnabledIds(pending);
            saved = new HashSet<string>(pending);

            UtilsCompileState.SyncFromConfig();
            AssetDatabase.Refresh();
        }

        // ---------------------------------------------------------------
        // 実行時トグルへのリフレクション
        // ---------------------------------------------------------------

        /// <summary>読み込み済みアセンブリから型を探す。見つからなければ null（＝無効化されている）。</summary>
        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        /// <summary>約束事：<c>static bool Enabled { get; }</c> を持つ。</summary>
        private static bool? ReadEnabled(Type type)
        {
            try
            {
                PropertyInfo property = type.GetProperty("Enabled", AnyStatic);
                if (property != null && property.PropertyType == typeof(bool))
                    return (bool)property.GetValue(null);
            }
            catch
            {
                // 下の null へ落ちる。
            }
            return null;
        }

        /// <summary>
        /// 約束事：<c>static void SetEnabled(bool)</c> を持つ。
        /// EditorPrefs を直に書かないのは、各機能が保存と同時にメニューのチェックや
        /// 表示の作り直しまで面倒を見ているため。そこを迂回すると状態がずれる。
        /// </summary>
        private static void WriteEnabled(Type type, bool value)
        {
            try
            {
                MethodInfo method = type.GetMethod("SetEnabled", AnyStatic, null, new[] { typeof(bool) }, null);
                if (method == null)
                {
                    Debug.LogWarning($"[YozoLab Utils] {type.FullName} に SetEnabled(bool) がありません。");
                    return;
                }
                method.Invoke(null, new object[] { value });
            }
            catch (Exception e)
            {
                Debug.LogError($"[YozoLab Utils] {type.FullName} の切り替えに失敗しました: {e.Message}");
            }
        }
    }
}
