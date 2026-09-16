// 診断専用。何が画面の大きな矩形を塗っているのかを記録する。
//
// スキンのスタイルもテーマシートもパネルのクリア色も原色にしたのに、窓の中身が
// Unity 既定の灰色のまま動かなかった。どれでもないなら何なのかを、推測ではなく
// 描画の入口で捕まえて確かめるためのもの。
//
// 環境変数 YOZOLAB_TRACE_PAINT=1 のときだけ動く。利用者の環境では何もしない。
#if YOZOLAB_EDITORTHEME_HARMONY
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace YozoLab.EditorTheme
{
    internal static class PaintTracer
    {
        private const string Id = "net.yozolab.editortheme.painttracer";
        private const int MaxLogs = 1500;
        private const float MinArea = 1200f;

        private static Harmony harmony;
        private static readonly HashSet<string> Seen = new HashSet<string>();
        private static int logged;

        public static bool Enabled => Environment.GetEnvironmentVariable("YOZOLAB_TRACE_PAINT") == "1";

        public static void Apply()
        {
            if (harmony != null || !Enabled) return;
            harmony = new Harmony(Id);

            int styles = 0, textures = 0;
            foreach (MethodInfo m in typeof(GUIStyle).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (m.Name != "Draw" || m.IsAbstract) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].ParameterType != typeof(Rect) || ps[0].Name != "position") continue;
                try { harmony.Patch(m, new HarmonyMethod(typeof(PaintTracer), nameof(OnStyleDraw))); styles++; }
                catch (Exception) { }
            }

            foreach (MethodInfo m in typeof(GUI).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "DrawTexture") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length < 2 || ps[0].ParameterType != typeof(Rect) || ps[0].Name != "position") continue;
                if (ps[1].Name != "image") continue;
                try { harmony.Patch(m, new HarmonyMethod(typeof(PaintTracer), nameof(OnDrawTexture))); textures++; }
                catch (Exception) { }
            }

            Debug.Log($"[TRACE] paint tracer on: GUIStyle.Draw x{styles}, GUI.DrawTexture x{textures}");
        }

        private static void OnStyleDraw(GUIStyle __instance, Rect position)
        {
            Texture2D bg = __instance?.normal?.background;
            Record($"style '{__instance?.name}' bg={(bg ? bg.name : "null")}", position);
        }

        private static void OnDrawTexture(Rect position, Texture image)
        {
            Record($"texture '{(image ? image.name : "null")}'", position);
        }

        private static void Record(string what, Rect r)
        {
            if (logged >= MaxLogs) return;
            if (r.width * r.height < MinArea) return;

            string key = $"{what}|{(int)r.width}x{(int)r.height}";
            if (!Seen.Add(key)) return;

            logged++;
            Debug.Log($"[TRACE] {what} rect={(int)r.x},{(int)r.y} {(int)r.width}x{(int)r.height} "
                      + $"guiColor=#{ColorUtility.ToHtmlStringRGBA(GUI.color)} bgColor=#{ColorUtility.ToHtmlStringRGBA(GUI.backgroundColor)}");
        }
    }
}
#endif
