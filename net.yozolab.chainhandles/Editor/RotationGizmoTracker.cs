using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// 回転ギズモ（Handles.RotationHandle）の戻り値から、1 フレーム分の回転を取り出す。
    ///
    /// Unity の回転ギズモは部品によって戻し方が違う（2022.3 で確認）。
    /// - 軸ごとの円（Disc）: 掴んだ時点に渡された向きを起点にした「累積」を返す。
    /// - 中央の自由回転（FreeRotate）: そのフレームに渡された向きへの「差分」を掛けて返す。
    ///
    /// 前回渡した向きを覚えておき、戻り値との差だけを取り出せば、どちらでも正しい差分になる。
    /// 毎回同じ向き（Global のときの identity など）を渡して戻り値を差分とみなすと、
    /// 円のほうでは累積を毎フレーム丸ごと掛け直すことになり、回転が膨らんでいく。
    /// </summary>
    internal sealed class RotationGizmoTracker
    {
        private Quaternion? _shown;

        /// <summary>今回ギズモに渡す向き。回していなければ initial。</summary>
        internal Quaternion Shown(Quaternion initial) => _shown ?? initial;

        /// <summary>ギズモの戻り値から、このフレームで回った分（世界空間）を取り出す。</summary>
        internal Quaternion Take(Quaternion shown, Quaternion returned)
        {
            // 持ち回すうちに長さが 1 から崩れないよう、覚える向きも差分も正規化しておく
            // （Quaternion.Inverse は単位四元数を前提にした共役なので、崩れると誤差が膨らむ）。
            returned = Normalize(returned);
            _shown = returned;
            return Normalize(returned * Quaternion.Inverse(Normalize(shown)));
        }

        private static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m < 1e-12f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        /// <summary>離したとき。次に掴んだら、また initial から始める。</summary>
        internal void Reset() => _shown = null;
    }
}
