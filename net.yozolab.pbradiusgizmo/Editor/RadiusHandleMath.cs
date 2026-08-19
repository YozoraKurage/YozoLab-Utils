using UnityEngine;

namespace YozoLab.PBRadiusGizmo
{
    /// <summary>
    /// ハンドル操作の計算のうち、VRChat SDK にも Unity のシーンにも依存しない部分。
    ///
    /// ここだけ独立させてあるのは、シーン上の操作は目で見るしか確かめようがなく、
    /// 数値の取り違え（世界座標の半径とインスペクタの Radius の混同など）が
    /// 一番起きやすいのがこの計算だから。
    /// </summary>
    internal static class RadiusHandleMath
    {
        /// <summary>
        /// 世界座標での半径を、インスペクタに書き戻す Radius へ戻す。
        ///
        /// SDK の描く半径は <c>Radius × radiusCurve の値 × ボーンのスケール</c>。
        /// 後ろ 2 つをまとめたものが <paramref name="factor"/>。
        /// factor が 0（カーブが 0、またはスケールが 0）だと逆算できないので、
        /// その場合は値を動かさない意味で 0 を返す。呼ぶ側でハンドル自体を出さない。
        /// </summary>
        internal static float BaseRadiusFromWorld(float worldRadius, float factor)
        {
            if (factor <= 0f || float.IsNaN(factor) || float.IsInfinity(factor)) return 0f;
            float value = worldRadius / factor;
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Mathf.Max(0f, value);
        }

        /// <summary>step 刻みに丸める。step が 0 以下ならそのまま返す。</summary>
        internal static float Snap(float value, float step)
        {
            if (step <= 0f) return value;
            return Mathf.Round(value / step) * step;
        }

        /// <summary>
        /// ハンドルを伸ばす向きを、ボーン軸に直交する 2 軸から選ぶ。
        ///
        /// 選び方は 2 段階。
        ///   1) 視線と平行な軸はドラッグしても画面上でほとんど動かないので、
        ///      視線に対してより直交している方を採る。
        ///   2) 画面の右向きに合わせて符号を決める。「右へ引くと大きくなる」を
        ///      いつも同じにしておかないと、カメラを回すたびに操作感が反転する。
        ///
        /// ドラッグ中は呼ばない（掴んだ向きを保つ）。
        /// </summary>
        internal static Vector3 PickHandleDirection(
            Vector3 axisA, Vector3 axisB, Vector3 viewForward, Vector3 viewRight)
        {
            Vector3 a = Normalize(axisA);
            Vector3 b = Normalize(axisB);
            Vector3 forward = Normalize(viewForward);

            if (a == Vector3.zero && b == Vector3.zero) return Vector3.right;
            if (a == Vector3.zero) a = b;
            if (b == Vector3.zero) b = a;

            Vector3 best = ScreenVisibility(a, forward) >= ScreenVisibility(b, forward) ? a : b;
            if (Vector3.Dot(best, Normalize(viewRight)) < 0f) best = -best;
            return best;
        }

        /// <summary>視線に直交しているほど 1 に近い。</summary>
        private static float ScreenVisibility(Vector3 dir, Vector3 viewForward)
        {
            if (viewForward == Vector3.zero) return 1f;
            return 1f - Mathf.Abs(Vector3.Dot(dir, viewForward));
        }

        private static Vector3 Normalize(Vector3 v)
        {
            float magnitude = v.magnitude;
            return magnitude > 1e-6f ? v / magnitude : Vector3.zero;
        }
    }
}
