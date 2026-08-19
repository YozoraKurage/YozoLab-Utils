using System;
using System.Collections.Generic;
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

        // ---------------------------------------------------------------
        // radiusCurve のキー操作
        // ---------------------------------------------------------------

        /// <summary>時刻の一致とみなす幅。CalcTransformRatio の値は 0..1。</summary>
        internal const float KeyEpsilon = 1e-4f;

        /// <summary>
        /// ボーンごとのチェーン位置すべてに、現在値のキーを打ったカーブを作る。
        ///
        /// カーブは連続補間なので、1 点だけ動かすと隣のボーンまでつられて動く。
        /// 「掴んだボーンだけ変える」を成り立たせるには、掴んだ瞬間に全ボーンの
        /// 位置へ現在値のキーを打ち、各ボーンをその場に固定する必要がある。
        /// 手書きの滑らかなカーブはボーン数ぶんのキーに置き換わるが、
        /// 描画・当たり判定が参照するのはボーン位置の値だけなので、見た目と
        /// 挙動はキーを打つ前と変わらない。
        /// </summary>
        internal static AnimationCurve BuildPerBoneKeys(
            IReadOnlyList<float> ratios, Func<float, float> valueAt)
        {
            var times = new List<float>(ratios.Count);
            for (int i = 0; i < ratios.Count; i++)
            {
                float t = ratios[i];
                bool duplicate = false;
                for (int j = 0; j < times.Count; j++)
                {
                    if (Mathf.Abs(times[j] - t) <= KeyEpsilon) { duplicate = true; break; }
                }
                if (!duplicate) times.Add(t);
            }
            times.Sort();

            var keys = new Keyframe[times.Count];
            for (int i = 0; i < times.Count; i++)
                keys[i] = new Keyframe(times[i], valueAt(times[i]));

            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        /// <summary>時刻 t のキーを探す。無ければ -1。</summary>
        internal static int FindKeyIndex(AnimationCurve curve, float t)
        {
            if (curve == null) return -1;
            for (int i = 0; i < curve.length; i++)
            {
                if (Mathf.Abs(curve[i].time - t) <= KeyEpsilon) return i;
            }
            return -1;
        }

        /// <summary>キー 1 つの値だけ差し替えたカーブを返す（元は触らない）。</summary>
        internal static AnimationCurve WithKeyValue(AnimationCurve curve, int index, float value)
        {
            Keyframe[] keys = curve.keys;
            Keyframe key = keys[index];
            key.value = value;
            keys[index] = key;

            var result = new AnimationCurve(keys);
            result.SmoothTangents(index, 0f);
            return result;
        }
    }
}
