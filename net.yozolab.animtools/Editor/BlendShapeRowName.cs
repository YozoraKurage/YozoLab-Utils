// この機能は Harmony（0Harmony.dll）に依存します。
// Harmony を同梱する VRCSDK（com.vrchat.base）がある環境でだけコンパイルされ、
// 判定は asmdef の versionDefines に任せています（手動設定は不要）。
// Harmony が無い環境ではこのファイルは空になり、コンパイルは通ります。
#if YOZOLAB_ANIMTOOLS_HARMONY
using System;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace YozoLab.AnimTools
{
    /// <summary>
    /// Animation ウィンドウのブレンドシェイプ行から、毎行くり返される
    /// 「Skinned Mesh Renderer.Blend Shape.」の前置きを取り除く機能。
    ///
    /// 目的：表情を打ち込む間、行の大半をこの定型句が占める。どの行も同じ前置きなので
    /// 識別には何も寄与せず、肝心のシェイプ名を右へ押し出して読みにくくしている。
    ///
    /// 実装：Unity は表示名を <c>AnimationWindowUtility.GetNicePropertyDisplayName(Type, string)</c>
    /// で <c>ObjectNames.NicifyVariableName(型名) + "." + 整形したプロパティ名</c> として
    /// 組み立てる。ここを Postfix し、SkinnedMeshRenderer の blendShape.* に限って
    /// シェイプ名だけを返す。
    ///
    /// 返すのは Nicify を通さない生のシェイプ名。シェイプ名は作者が付けた固有名で
    /// （"vrc.v_aa" など）、変数名として整形すると別物になってしまう。インスペクタ側の
    /// 表示とも、この方が一致する。
    ///
    /// メニュー <c>YozoLab/Animation/ブレンドシェイプ名を短く表示</c> で切り替え可能。
    /// 既定は ON。内部 API 依存のため、失敗しても既存の表示を壊さないよう握りつぶす。
    /// </summary>
    [InitializeOnLoad]
    internal static class BlendShapeRowName
    {
        private const string MenuPath = "YozoLab/Animation/ブレンドシェイプ名を短く表示";
        private const string Pref = "YozoLab.AnimTools.BlendShapeRowName.Enabled";
        private const string HarmonyId = "net.yozolab.animtools.blendshaperowname";
        private const string BlendShapePrefix = "blendShape.";

        /// <summary>機能の有効/無効。パッチは常駐し、OFF のときは即 no-op。</summary>
        public static bool Enabled { get; private set; }

        private static bool patched;

        static BlendShapeRowName()
        {
            Enabled = EditorPrefs.GetBool(Pref, true);
            EditorApplication.delayCall += () => Menu.SetChecked(MenuPath, Enabled);
            TryApplyPatch();
        }

        // ---------------------------------------------------------------
        // メニュー（トグル）
        // ---------------------------------------------------------------

        [MenuItem(MenuPath, false, 102)]
        private static void Toggle() => SetEnabled(!Enabled);

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void SetEnabled(bool value)
        {
            Enabled = value;
            EditorPrefs.SetBool(Pref, Enabled);
            Menu.SetChecked(MenuPath, Enabled);

            if (Enabled && !patched)
            {
                TryApplyPatch();
                if (!patched)
                    Debug.LogWarning("[BlendShapeRowName] 表示名へのパッチに失敗しているため機能しません。");
            }

            // 表示名は階層を組み立てる時点でノードに焼き付くので、作り直させる。
            AnimationWindowRefresher.RefreshAll();
        }

        // ---------------------------------------------------------------
        // Harmony パッチ適用
        // ---------------------------------------------------------------

        private static void TryApplyPatch()
        {
            if (patched) return;
            try
            {
                Type utility = typeof(Editor).Assembly.GetType("UnityEditorInternal.AnimationWindowUtility");
                MethodInfo displayName = utility?.GetMethod("GetNicePropertyDisplayName",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Type), typeof(string) }, null);
                if (displayName == null)
                {
                    Debug.LogWarning("[BlendShapeRowName] AnimationWindowUtility.GetNicePropertyDisplayName が見つかりませんでした（Unity のバージョン差異）。機能を無効化します。");
                    return;
                }

                var harmony = new Harmony(HarmonyId);
                harmony.Patch(
                    displayName,
                    postfix: new HarmonyMethod(typeof(BlendShapeRowName).GetMethod(nameof(DisplayNamePostfix),
                        BindingFlags.Static | BindingFlags.NonPublic)));
                patched = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BlendShapeRowName] パッチ適用に失敗しました（機能は無効のまま）: {e.Message}");
            }
        }

        /// <summary>
        /// SkinnedMeshRenderer の blendShape.* だけ、前置きを落としてシェイプ名だけにする。
        /// それ以外の型・プロパティには一切触らない。
        /// </summary>
        private static void DisplayNamePostfix(Type animatableObjectType, string propertyName, ref string __result)
        {
            if (!Enabled) return;
            try
            {
                if (animatableObjectType != typeof(SkinnedMeshRenderer)) return;
                if (propertyName == null || !propertyName.StartsWith(BlendShapePrefix, StringComparison.Ordinal)) return;

                string shapeName = propertyName.Substring(BlendShapePrefix.Length);
                if (shapeName.Length > 0) __result = shapeName;
            }
            catch
            {
                // 表示名の組み立て中に落ちると Animation ウィンドウが壊れるので握りつぶす。
            }
        }
    }
}
#endif // YOZOLAB_ANIMTOOLS_HARMONY
