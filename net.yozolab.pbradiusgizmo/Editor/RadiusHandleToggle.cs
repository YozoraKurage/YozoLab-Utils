// VRChat SDK（com.vrchat.base）がある環境でだけコンパイルされる。
// Harmony（0Harmony.dll）も SDK 同梱のものを使う。
#if YOZOLAB_PBRADIUS_VRCSDK
using System;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace YozoLab.PBRadiusGizmo
{
    /// <summary>
    /// Radius ハンドルを出す PhysBone を、その PhysBone のインスペクタで選ばせる。
    ///
    /// ハンドルは出ているだけで毎フレームの組み立てと当たり判定を伴う。
    /// 選択階層に PhysBone が多いと、触っていないハンドルのコストが積み上がるので、
    /// 既定は全部 OFF にして「今まさに調整したい PhysBone」だけ ON にしてもらう。
    ///
    /// トグルの置き場はコンポーネント自身のインスペクタが一番迷わないため、
    /// VRCPhysBoneEditor.OnInspectorGUI に Harmony の postfix で 1 行足す。
    /// パッチを当てられない環境ではトグルを出せないので、従来どおり
    /// 「全部 ON」へ倒す（ハンドルが出せなくなるよりは重いほうがまし）。
    ///
    /// 状態はセッション限り（SessionState）。シーンやエディタを開き直したら
    /// OFF に戻る。作業中の一時的な道具なので、アセットには何も残さない。
    /// </summary>
    [InitializeOnLoad]
    internal static class RadiusHandleToggle
    {
        private const string HarmonyId = "net.yozolab.pbradiusgizmo";
        private const string EditorTypeName = "VRC.SDK3.Dynamics.PhysBone.VRCPhysBoneEditor";
        private const string SessionKey = "YozoLab.PBRadiusGizmo.HandleTargets";

        private static readonly System.Collections.Generic.HashSet<int> EnabledIds =
            new System.Collections.Generic.HashSet<int>();

        private static Harmony _harmony;

        /// <summary>パッチが当たっているか。外れているときは全 PhysBone を有効扱いにする。</summary>
        internal static bool Installed => _harmony != null;

        static RadiusHandleToggle()
        {
            foreach (int id in SessionState.GetIntArray(SessionKey, Array.Empty<int>()))
                EnabledIds.Add(id);

            // ドメインリロード直後は他アセンブリの読み込みが終わっていないことがある。
            EditorApplication.delayCall += Install;
            AssemblyReloadEvents.beforeAssemblyReload += Uninstall;
        }

        internal static bool IsEnabled(VRCPhysBoneBase pb)
        {
            if (_harmony == null) return true; // トグルを出せない環境では従来どおり
            return pb != null && EnabledIds.Contains(pb.GetInstanceID());
        }

        internal static void SetEnabled(VRCPhysBoneBase pb, bool value)
        {
            if (pb == null) return;

            bool changed = value
                ? EnabledIds.Add(pb.GetInstanceID())
                : EnabledIds.Remove(pb.GetInstanceID());
            if (!changed) return;

            var ids = new int[EnabledIds.Count];
            EnabledIds.CopyTo(ids);
            SessionState.SetIntArray(SessionKey, ids);
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------
        // インスペクタへの差し込み
        // ---------------------------------------------------------------

        private static void Install()
        {
            if (_harmony != null) return;

            try
            {
                Type editorType = AccessTools.TypeByName(EditorTypeName);
                MethodInfo target = editorType == null
                    ? null
                    : AccessTools.Method(editorType, "OnInspectorGUI");

                if (target == null)
                {
                    Debug.LogWarning(
                        $"[PhysBone Radius Gizmo] {EditorTypeName}.OnInspectorGUI が見つかりません。"
                        + "ハンドルの個別トグルは出せないため、全ての PhysBone でハンドルを出します。");
                    return;
                }

                var harmony = new Harmony(HarmonyId);
                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(RadiusHandleToggle).GetMethod(
                        nameof(OnInspectorGUI_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
                _harmony = harmony;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[PhysBone Radius Gizmo] インスペクタへのトグル追加に失敗しました（全ての PhysBone でハンドルを出します）: {e.Message}");
            }
        }

        private static void Uninstall()
        {
            if (_harmony == null) return;
            try { _harmony.UnpatchAll(HarmonyId); }
            catch (Exception e)
            {
                Debug.LogWarning($"[PhysBone Radius Gizmo] パッチを外せませんでした: {e.Message}");
            }
            _harmony = null;
        }

        private static void OnInspectorGUI_Postfix(object __instance)
        {
            try
            {
                if (!PhysBoneRadiusGizmo.Enabled) return;
                if (!(__instance is Editor editor)) return;

                UnityEngine.Object[] targets = editor.targets;
                if (targets.Length == 0 || !(targets[0] is VRCPhysBoneBase first)) return;

                bool value = IsEnabled(first);
                bool mixed = false;
                for (int i = 1; i < targets.Length; i++)
                {
                    if (targets[i] is VRCPhysBoneBase pb && IsEnabled(pb) != value)
                    {
                        mixed = true;
                        break;
                    }
                }

                EditorGUILayout.Space(2);
                EditorGUI.showMixedValue = mixed;
                EditorGUI.BeginChangeCheck();
                bool next = EditorGUILayout.ToggleLeft(
                    new GUIContent("Radius ハンドルをシーンに出す",
                        "Collision Radius をシーン上のハンドルで調整する。ON にした PhysBone だけに出る。"),
                    value);
                EditorGUI.showMixedValue = false;

                if (EditorGUI.EndChangeCheck())
                {
                    for (int i = 0; i < targets.Length; i++)
                    {
                        if (targets[i] is VRCPhysBoneBase pb) SetEnabled(pb, next);
                    }
                }
            }
            catch (Exception)
            {
                // インスペクタ本体を巻き込まない。ここで失敗しても描画は続行させる。
            }
        }
    }
}
#endif
