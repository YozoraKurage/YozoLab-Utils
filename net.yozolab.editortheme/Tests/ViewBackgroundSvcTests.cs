using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YozoLab.EditorTheme;

namespace YozoLab.Tests
{
    /// <summary>
    /// ビューの地色 kViewBackgroundColor が、こちらの差し替えを反映するのかを実測する。
    /// これは static readonly な SVC で、型初期化のときに作られる。値を一度読んで
    /// 抱え込むなら、あとからこちらが手を入れても古い灰色のまま残る。
    /// </summary>
    public class ViewBackgroundSvcTests
    {
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        private static Color ReadSvc(object svc)
        {
            PropertyInfo value = svc.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return (Color)value.GetValue(svc);
        }

        [Test]
        public void ViewBackgroundFollowsThePatch()
        {
            object svc = typeof(EditorGUIUtility).GetField("kViewBackgroundColor", Stat)?.GetValue(null);
            Assert.That(svc, Is.Not.Null, "kViewBackgroundColor が見つからない");
            Debug.Log($"[SVC] type={svc.GetType()}");
            foreach (FieldInfo f in svc.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Debug.Log($"[SVC] field {f.FieldType.Name} {f.Name} = {f.GetValue(svc)}");

            EditorThemeApplier.SetEnabled(false);
            Color off = ReadSvc(svc);
            MethodInfo def = typeof(EditorGUIUtility).GetMethod("GetDefaultBackgroundColor", Stat);
            Debug.Log($"[SVC] theme off: kViewBackgroundColor={Hex(off)} GetDefaultBackgroundColor={Hex((Color)def.Invoke(null, null))}");

            EditorThemeApplier.SetEnabled(true);
            Color on = ReadSvc(svc);
            Debug.Log($"[SVC] theme on : kViewBackgroundColor={Hex(on)} GetDefaultBackgroundColor={Hex((Color)def.Invoke(null, null))}");

            Debug.Log(on == off
                ? "[SVC] 変化なし。ビューの地色はこちらの差し替えを見ていない"
                : "[SVC] 追随した");

            // 素の kViewBackgroundColor は #282828。画面を覆っている灰色 #383838 とは
            // 別物なので、あの灰色はこの値ではない。取り違えないよう、ここで固定しておく。
            Assert.That(ColorUtility.ToHtmlStringRGB(off), Is.Not.EqualTo("383838"),
                "kViewBackgroundColor が画面の地の灰色と同じになった。前提を見直すこと");
            Assert.That(on, Is.Not.EqualTo(off), "ビューの地色がこちらの差し替えに追随しなくなった");
        }
    }
}
