using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// Transform 階層の「今の姿勢」を 1 つの整数にまとめる。
    ///
    /// ギズモをキャッシュするうえで一番効くのが、ボーンが動いたことに気付けるか。
    /// 変更イベント（ObjectChangeEvents 等）はインスペクタ操作やシーンでの移動は
    /// 拾えるが、他ツールのプレビューやコンストレイントのように、イベントを出さずに
    /// Transform が動く経路は拾えない。時間で諦める作りにすると、その分だけ
    /// ギズモが遅れて見える。
    ///
    /// 毎フレーム全ボーンを見るので、1 本あたりのコストが効く。
    ///   - 階層の形は滅多に変わらないので、Transform の並びを控えておいて使い回す
    ///     （childCount / GetChild の呼び出しが毎フレーム消える）
    ///   - 位置と回転は 1 回の呼び出しでまとめて取る
    /// 階層そのものが変わった場合は InvalidationVersion が上がるので、そこで作り直す。
    /// </summary>
    internal static class TransformFingerprint
    {
        private sealed class Walk
        {
            internal Transform[] transforms;
            internal int version = -1;
        }

        private static readonly Dictionary<int, Walk> Walks = new Dictionary<int, Walk>();
        private static readonly Stack<Transform> Pending = new Stack<Transform>(256);
        private static readonly List<Transform> Collected = new List<Transform>(256);

        internal static void Clear() => Walks.Clear();

        /// <summary>2 つの階層をまとめて 1 つの指紋にする（同じものなら 1 回で済ませる）。</summary>
        internal static int Compute(Transform a, Transform b)
        {
            int hash = Compute(a);
            if (b != null && b != a)
            {
                unchecked { hash = hash * 31 + Compute(b); }
            }
            return hash;
        }

        internal static int Compute(Transform root)
        {
            if (root == null) return 0;

            var transforms = GetTransforms(root);

            unchecked
            {
                int hash = 17 + transforms.Length;

                for (int i = 0; i < transforms.Length; i++)
                {
                    var t = transforms[i];
                    if (t == null)
                    {
                        // 階層が壊れている。作り直させる。
                        Walks.Remove(root.GetInstanceID());
                        return hash * 31 + i;
                    }

                    t.GetLocalPositionAndRotation(out var p, out var r);
                    var s = t.localScale;

                    hash = hash * 31 + p.x.GetHashCode();
                    hash = hash * 31 + p.y.GetHashCode();
                    hash = hash * 31 + p.z.GetHashCode();
                    hash = hash * 31 + r.x.GetHashCode();
                    hash = hash * 31 + r.y.GetHashCode();
                    hash = hash * 31 + r.z.GetHashCode();
                    hash = hash * 31 + r.w.GetHashCode();
                    hash = hash * 31 + s.x.GetHashCode();
                    hash = hash * 31 + s.y.GetHashCode();
                    hash = hash * 31 + s.z.GetHashCode();
                }

                return hash;
            }
        }

        /// <summary>
        /// 階層の並びを控えておく。形が変わったときだけ作り直す
        /// （形の変化は hierarchyChanged で版数が上がる）。
        /// </summary>
        private static Transform[] GetTransforms(Transform root)
        {
            int id = root.GetInstanceID();

            if (Walks.TryGetValue(id, out var walk)
                && walk.version == InvalidationVersion.Current)
            {
                return walk.transforms;
            }

            Collected.Clear();
            Pending.Clear();
            Pending.Push(root);

            while (Pending.Count > 0)
            {
                var t = Pending.Pop();
                Collected.Add(t);

                int count = t.childCount;
                for (int i = 0; i < count; i++) Pending.Push(t.GetChild(i));
            }

            if (walk == null)
            {
                walk = new Walk();
                Walks[id] = walk;
            }

            walk.transforms = Collected.ToArray();
            walk.version = InvalidationVersion.Current;
            return walk.transforms;
        }
    }
}
