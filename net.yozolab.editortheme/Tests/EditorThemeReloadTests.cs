using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YozoLab.EditorTheme;

namespace YozoLab.Tests
{
    /// <summary>
    /// ドメインリロードをまたいだときに、テーマの差し替えがどうなるかを実測する。
    /// 実機で「static コピーの背景が null」になった原因の切り分け用。
    /// </summary>
    public class EditorThemeReloadTests
    {
        private const string Stage = "YozoLab.EditorTheme.Tests.Stage";
        private const string TexId = "YozoLab.EditorTheme.Tests.TexId";
        private const string SkinBg = "YozoLab.EditorTheme.Tests.SkinBg";
        private const string CopyBg = "YozoLab.EditorTheme.Tests.CopyBg";

        private static GUIStyle HostViewBackground()
        {
            Type styles = typeof(Editor).Assembly.GetType("UnityEditor.HostView+Styles");
            return styles?.GetField("background", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as GUIStyle;
        }

        private static string Describe(Texture2D texture)
            => texture ? $"{texture.name}#{texture.GetInstanceID()}" : "null";

        private static string Describe(GUIStyle style)
            => style == null ? "?" : $"'{style.name}' bg={Describe(style.normal.background as Texture2D)}";

        private static string SkinHostviewBg()
        {
            GUISkin skin = ImguiSkinPatcher.EditorDefaultSkin();
            GUIStyle style = skin == null ? null : skin.FindStyle("hostview");
            return style == null ? "?" : Describe(style.normal.background as Texture2D);
        }

        [UnityTest]
        public IEnumerator AppliedThemeSurvivesDomainReload()
        {
            int stage = SessionState.GetInt(Stage, 0);
            if (stage == 0)
            {
                EditorThemeApplier.SetEnabled(true);
                Assert.That(EditorThemeApplier.IsApplied, Is.True, "適用されていない");

                // GUI の中と同じ状態を作ってコピーへ当てる。
                ImguiSkinPatcher.RunPendingInGui();

                GUIStyle copy = HostViewBackground();
                Assert.That(copy, Is.Not.Null);
                string skinBg = SkinHostviewBg();
                string copyBg = Describe(copy.normal.background as Texture2D);
                Debug.Log($"[RELOAD] before: skin hostview={skinBg} copy={Describe(copy)}");
                Assert.That(copy.name, Is.EqualTo("hostview"), "コピーがスキン外で作られている");
                Assert.That(copyBg, Does.StartWith("YozoLab.EditorTheme.Solid"), "リロード前の時点でコピーに当たっていない");

                SessionState.SetInt(TexId, (copy.normal.background as Texture2D).GetInstanceID());
                SessionState.SetString(SkinBg, skinBg);
                SessionState.SetString(CopyBg, copyBg);
                SessionState.SetInt(Stage, 1);
                EditorUtility.RequestScriptReload();
            }

            yield return stage == 0 ? new WaitForDomainReload() : null;

            // ── 新しいドメイン ──
            SessionState.SetInt(Stage, 0);
            int texId = SessionState.GetInt(TexId, 0);
            var tex = EditorUtility.InstanceIDToObject(texId) as Texture2D;
            string skinNow = SkinHostviewBg();
            Debug.Log($"[RELOAD] after reload (immediately): applied={EditorThemeApplier.IsApplied} solid#{texId} alive={(tex ? "yes" : "no")} skin hostview={skinNow} (was {SessionState.GetString(SkinBg, "?")})");

            // delayCall の Apply が走るのを待つ。
            for (int i = 0; i < 5; i++) yield return null;
            Debug.Log($"[RELOAD] after delayCall: applied={EditorThemeApplier.IsApplied} skin hostview={SkinHostviewBg()}");

            ImguiSkinPatcher.RunPendingInGui();
            GUIStyle copyNow = HostViewBackground();
            string copyNowBg = copyNow == null ? "?" : Describe(copyNow.normal.background as Texture2D);
            Debug.Log($"[RELOAD] after apply in GUI: copy={Describe(copyNow)}");

            Assert.That(EditorThemeApplier.IsApplied, Is.True, "リロード後に再適用されていない");
            Assert.That(copyNowBg, Does.StartWith("YozoLab.EditorTheme.Solid"), "リロード後、コピーに当たっていない");
            Debug.Log($"[RELOAD] old solid alive after reload: {(tex ? "yes" : "no")}");

            // テストが再開する頃には delayCall の再適用が済んでいるので、剥がれていたかは
            // 「無効化したとき本物のテクスチャに戻るか」で見る。リロード前に剥がれていなければ、
            // 新しいドメインは前のドメインの単色テクスチャを原本として控えているので、
            // 無効化しても単色のまま残る。
            try
            {
                EditorThemeApplier.SetEnabled(false);
                string restored = SkinHostviewBg();
                Debug.Log($"[RELOAD] after disable: skin hostview={restored}");
                Assert.That(restored, Does.Not.StartWith("YozoLab.EditorTheme.Solid"),
                    "リロード前に剥がれていない（無効化しても前のドメインの単色テクスチャが残る）");
                Assert.That(restored, Is.Not.EqualTo("null"), "無効化で hostview の背景が消えた");
            }
            finally
            {
                EditorThemeApplier.SetEnabled(true);
            }
        }

        [Test]
        public void SolidTexturesSurviveUnloadUnusedAssets()
        {
            EditorThemeApplier.SetEnabled(true);
            ImguiSkinPatcher.RunPendingInGui();
            GUIStyle copy = HostViewBackground();
            Assert.That(copy, Is.Not.Null);
            var before = copy.normal.background as Texture2D;
            Assert.That(copy.name, Is.EqualTo("hostview"), "コピーがスキン外で作られている");
            Assert.That(before, Is.Not.Null);

            EditorUtility.UnloadUnusedAssetsImmediate();
            var after = copy.normal.background as Texture2D;
            Debug.Log($"[UNLOAD] before={Describe(before)} after={Describe(after)} skin={SkinHostviewBg()}");
            Assert.That(after, Is.Not.Null, "UnloadUnusedAssets で単色テクスチャが消えた");
        }
    }
}
