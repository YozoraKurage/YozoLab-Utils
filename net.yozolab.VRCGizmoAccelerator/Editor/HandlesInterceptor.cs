using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// Harmony から呼ばれる prefix 群。
    ///
    /// どれも「バッチ区間の中にいるときだけ」横取りし、区間外では true を返して
    /// 元の実装をそのまま走らせる。つまり VRChat SDK のギズモ描画以外には
    /// 一切影響しない（他ツールの Handles 呼び出しも素通りする）。
    ///
    /// 頂点の作り方は元の実装と同じで、GL への発行だけを GizmoBatch へ回す。
    /// </summary>
    internal static class HandlesInterceptor
    {
        // Handles の円弧は固定 60 点で分割される（UnityEditor.Handles の
        // s_WireArcPoints が Vector3[60]）。ここも同じ分割数にしないと見た目が変わる。
        private const int ArcPoints = 60;

        /// <summary>テストから Unity 側の分割数と突き合わせるために公開している。</summary>
        internal static int ArcPointCount => ArcPoints;

        private static readonly Vector3[] ArcBuffer = new Vector3[ArcPoints];

        // ---- Handles ----------------------------------------------------------

        /// <summary>
        /// Handles.SetDiscSectionPoints と同じ点列を作る。
        ///
        /// 本家は「from * radius を normal 周りに 1 ステップずつ回す」実装だが、
        /// クォータニオン積を点ごとに回すと高くつく（実測 2000 円弧で 3.8ms）。
        /// ここでは回転を平面内の三角関数に落とし、加法定理で 1 ステップずつ
        /// 進める。normal 方向の成分は回転で変わらないので分けて扱えば、
        /// from が normal と直交していない場合も本家と同じ結果になる。
        /// </summary>
        internal static void SetDiscSectionPoints(Vector3[] dest, Vector3 center, Vector3 normal, Vector3 from, float angle, float radius)
        {
            from.Normalize();

            var axis = normal.normalized;
            var v = from * radius;

            // 回転で動かない成分と、平面内で回る成分に分ける
            var parallel = axis * Vector3.Dot(axis, v);
            var u = v - parallel;          // 開始位置（平面内）
            var w = Vector3.Cross(axis, u); // u と直交する平面内のベクトル

            float step = angle / (dest.Length - 1) * Mathf.Deg2Rad;
            float cosStep = Mathf.Cos(step);
            float sinStep = Mathf.Sin(step);

            var origin = center + parallel;
            float c = 1f, s = 0f;

            for (int i = 0; i < dest.Length; i++)
            {
                dest[i] = new Vector3(
                    origin.x + u.x * c + w.x * s,
                    origin.y + u.y * c + w.y * s,
                    origin.z + u.z * c + w.z * s);

                // 加法定理で 1 ステップ進める
                float nc = c * cosStep - s * sinStep;
                s = s * cosStep + c * sinStep;
                c = nc;
            }
        }

        private static void AppendArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius)
        {
            SetDiscSectionPoints(ArcBuffer, center, normal, from, angle, radius);
            GizmoBatch.AddLineStrip(ArcBuffer, 0, ArcPoints, Handles.color, Handles.matrix);
        }

        private static void AppendSolidArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius)
        {
            SetDiscSectionPoints(ArcBuffer, center, normal, from, angle, radius);

            var m = Handles.matrix;
            var color = Handles.color;
            var origin = m.MultiplyPoint3x4(center);
            var prev = m.MultiplyPoint3x4(ArcBuffer[0]);
            for (int i = 1; i < ArcPoints; i++)
            {
                var cur = m.MultiplyPoint3x4(ArcBuffer[i]);
                GizmoBatch.AddTriangle(origin, prev, cur, color);
                prev = cur;
            }
        }

        /// <summary>法線に対して直交する開始方向。Handles.DrawWireDisc と同じ選び方。</summary>
        private static Vector3 DiscFrom(Vector3 normal)
        {
            var from = Vector3.Cross(normal, Vector3.up);
            if (from.sqrMagnitude < 0.001f) from = Vector3.Cross(normal, Vector3.right);
            return from;
        }

        public static bool DrawLine_Prefix(Vector3 p1, Vector3 p2)
        {
            var mode = GizmoBatch.Mode;
            if (mode == GizmoBatch.Interception.PassThrough) return true;
            if (mode == GizmoBatch.Interception.Skip) return false;
            var m = Handles.matrix;
            GizmoBatch.AddLine(m.MultiplyPoint3x4(p1), m.MultiplyPoint3x4(p2), Handles.color);
            return false;
        }

        public static bool DrawWireArc_Prefix(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius)
        {
            var mode = GizmoBatch.Mode;
            if (mode == GizmoBatch.Interception.PassThrough) return true;
            if (mode == GizmoBatch.Interception.Skip) return false;
            AppendArc(center, normal, from, angle, radius);
            return false;
        }

        public static bool DrawWireDisc_Prefix(Vector3 center, Vector3 normal, float radius)
        {
            var mode = GizmoBatch.Mode;
            if (mode == GizmoBatch.Interception.PassThrough) return true;
            if (mode == GizmoBatch.Interception.Skip) return false;
            AppendArc(center, normal, DiscFrom(normal), 360f, radius);
            return false;
        }

        public static bool DrawSolidArc_Prefix(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius)
        {
            var mode = GizmoBatch.Mode;
            if (mode == GizmoBatch.Interception.PassThrough) return true;
            if (mode == GizmoBatch.Interception.Skip) return false;
            AppendSolidArc(center, normal, from, angle, radius);
            return false;
        }

        public static bool DrawSolidDisc_Prefix(Vector3 center, Vector3 normal, float radius)
        {
            var mode = GizmoBatch.Mode;
            if (mode == GizmoBatch.Interception.PassThrough) return true;
            if (mode == GizmoBatch.Interception.Skip) return false;
            AppendSolidArc(center, normal, DiscFrom(normal), 360f, radius);
            return false;
        }

        // ---- VRChat SDK の HandlesUtil ---------------------------------------
        //
        // 元の実装はどれも「点列バッファを offset から順に読み、GL.Begin/End で
        // 描いて、次の offset（または消費点数）を返す」形。返り値の規約が
        // メソッドごとに違うので、そこも含めてそのまま合わせる。

        /// <summary>線 1 本 = GL.LINES で 2 点。元の実装は消費点数 2 を返す。</summary>
        public static bool DrawLineBatched_Prefix(ref List<Vector3> buffer, int offset, Color color, ref int __result)
        {
            var mode = GizmoBatch.Mode;
            if (mode == GizmoBatch.Interception.PassThrough) return true;

            // 描かない場合でも、呼び出し側が進めるカーソルの値は元と同じにする。
            __result = 2;
            if (mode == GizmoBatch.Interception.Skip) return false;

            if (buffer == null || offset + 2 > buffer.Count)
            {
                __result = 2;
                return false;
            }

            GizmoBatch.AddLine(buffer[offset], buffer[offset + 1], color);
            __result = 2;
            return false;
        }

        /// <summary>球 = 25 点の連続線 × 3 リング。元の実装は offset + 75 を返す。</summary>
        public static bool DrawSphereBatched_Prefix(ref List<Vector3> buffer, int offset, Color color, Matrix4x4 matrix, ref int __result)
        {
            var mode = GizmoBatch.Mode;
            if (mode == GizmoBatch.Interception.PassThrough) return true;

            const int ring = 25;
            const int rings = 3;

            __result = offset + ring * rings;
            if (mode == GizmoBatch.Interception.Skip) return false;

            if (buffer == null || offset + ring * rings > buffer.Count)
            {
                __result = offset + ring * rings;
                return false;
            }

            int cursor = offset;
            for (int r = 0; r < rings; r++)
            {
                GizmoBatch.AddLineStrip(buffer, cursor, ring, color, matrix);
                cursor += ring;
            }

            __result = cursor;
            return false;
        }

        /// <summary>
        /// カプセル = 側面 4 本（GL.LINES で 2 点ずつ）+ 25 点リング × 2 +
        /// 13 点の半円 × 4。合計 110 点。元の実装は offset + 110 を返す。
        /// </summary>
        public static bool DrawCapsuleBatched_Prefix(ref List<Vector3> buffer, int offset, Color color, Matrix4x4 matrix, ref int __result)
        {
            var mode = GizmoBatch.Mode;
            if (mode == GizmoBatch.Interception.PassThrough) return true;

            const int sidePoints = 8;   // 2 点 × 4 本
            const int ring = 25;
            const int cap = 13;
            const int total = sidePoints + ring * 2 + cap * 4;

            __result = offset + total;
            if (mode == GizmoBatch.Interception.Skip) return false;

            if (buffer == null || offset + total > buffer.Count)
            {
                __result = offset + total;
                return false;
            }

            int cursor = offset;

            for (int i = 0; i < sidePoints; i += 2)
            {
                GizmoBatch.AddLine(
                    matrix.MultiplyPoint3x4(buffer[cursor + i]),
                    matrix.MultiplyPoint3x4(buffer[cursor + i + 1]),
                    color);
            }
            cursor += sidePoints;

            for (int r = 0; r < 2; r++)
            {
                GizmoBatch.AddLineStrip(buffer, cursor, ring, color, matrix);
                cursor += ring;
            }

            for (int c = 0; c < 4; c++)
            {
                GizmoBatch.AddLineStrip(buffer, cursor, cap, color, matrix);
                cursor += cap;
            }

            __result = cursor;
            return false;
        }

        /// <summary>
        /// 線 1 本ごとの ApplyWireMaterial は要らない（バッチを流すときに 1 回だけ当てる）。
        /// 元の実装は毎回 MethodInfo.Invoke しており、そこそこの割合を占める。
        /// </summary>
        public static bool ApplyWireMaterial_Prefix()
        {
            return GizmoBatch.Mode == GizmoBatch.Interception.PassThrough;
        }

        /// <summary>
        /// 図形を組み立てる側（HandlesUtil.DrawWireCapsule など）をまとめて止める。
        ///
        /// これらは中で Handles.DrawLine / DrawWireArc を何度も呼ぶが、その手前に
        /// 座標計算がある。描画イベントでないときは、葉っぱで捨てるより
        /// ここで丸ごと止めたほうが計算ごと省ける（OnSceneGUI は毎フレーム
        /// Layout → Repaint と複数回呼ばれるので、その分がそのまま浮く）。
        ///
        /// 描画イベントのときは何もせず元の実装を走らせる（中の描画は葉っぱ側で拾う）。
        /// </summary>
        public static bool SkipWhenNotDrawing_Prefix()
        {
            return GizmoBatch.Mode != GizmoBatch.Interception.Skip;
        }

        // ---- ギズモ本体の区間 ------------------------------------------------

        public static void GizmoScope_Prefix(MethodBase __originalMethod)
        {
            ScopeProfiler.Begin(__originalMethod);
            GizmoBatch.InGizmoCallback = true;
            GizmoScopes.Enter(null);
        }

        /// <summary>
        /// 対象コンポーネントを受け取れる版（引数名が script のもの）。
        /// 前回描いたものをそのまま使えるなら false を返し、SDK の Draw() を丸ごと飛ばす。
        /// </summary>
        public static bool GizmoScopeScript_Prefix(MethodBase __originalMethod, object script)
        {
            ScopeProfiler.Begin(__originalMethod);
            GizmoBatch.InGizmoCallback = true;
            return GizmoScopes.Enter(script as Object);
        }

        /// <summary>同上。引数名が target のもの。</summary>
        public static bool GizmoScopeTarget_Prefix(MethodBase __originalMethod, object target)
        {
            ScopeProfiler.Begin(__originalMethod);
            GizmoBatch.InGizmoCallback = true;
            return GizmoScopes.Enter(target as Object);
        }

        /// <summary>
        /// OnSceneGUI 用の区間。中身はギズモ用と同じだが、当てる相手が違うので分けてある。
        /// </summary>
        public static void GuiScope_Prefix(MethodBase __originalMethod)
        {
            ScopeProfiler.Begin(__originalMethod);

            // OnSceneGUI はギズモのコールバックではないので Gizmos.* は使えない。
            // ここから始まる区間は即時描画で出す。
            GizmoBatch.InGizmoCallback = false;
            GizmoScopes.Enter(null);
        }

        /// <summary>
        /// finalizer にしているのは、元のギズモ描画が例外を投げても必ず閉じるため。
        /// ここで閉じ損ねると以降の Handles 呼び出しを溜め込み続けてしまう。
        /// </summary>
        public static void GizmoScope_Finalizer()
        {
            GizmoScopes.Exit();
            ScopeProfiler.End();
        }

        /// <summary>
        /// HandleUtility.GetHandleSize はカメラとの距離で決まる。捕捉中にこれが
        /// 参照されたら、その結果はカメラを動かすと変わるのでキャッシュできない。
        /// 値そのものには手を触れず、印だけ付けて元の実装に通す。
        /// </summary>
        public static bool GetHandleSize_Prefix()
        {
            GizmoBatch.MarkCameraDependent();
            return true;
        }
    }
}
