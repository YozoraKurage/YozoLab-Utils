using System;
using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// 1 つのオブジェクトから「作れそうな鎖」を列挙する。
    ///
    /// 選んだオブジェクト X に対して、鎖の始点は次の 2 通りを考える。
    /// - X 自身
    /// - X から親を辿り、親が一本道（子が 1 つだけ）である限り遡った先（房の根元）
    ///   髪の毛先や途中の骨を選んだときに、房の根元から張れるようにするため。
    ///
    /// 終点は X の子孫のうち、末端（子が無い）と枝分かれ点（子が 2 つ以上）。
    /// 枝分かれ点までの鎖は「背骨を胸まで」のような使い方を拾う。
    /// 根元から遡った場合は X 自身も終点の候補に入れる（毛先を選んだとき用）。
    ///
    /// 並びは「根元から末端」を先頭に、ありそうな順にする。
    /// 木の辿り方は差し替えられるようにしてあり、テストは偽の木で行う。
    /// </summary>
    internal static class ChainCandidates
    {
        /// <summary>候補の上限。枝の多い木（Hips など）を選んだときに一覧が溢れないように。</summary>
        internal const int MaxCandidates = 64;

        internal readonly struct Candidate<T>
        {
            public readonly T start;
            public readonly T end;

            public Candidate(T start, T end)
            {
                this.start = start;
                this.end = end;
            }
        }

        /// <summary>
        /// 木の形だけを見て候補を並べる。
        /// </summary>
        /// <param name="usable">関節として使えるノードか。使えないノードの先は辿らない。</param>
        internal static List<Candidate<T>> Find<T>(
            T selected,
            Func<T, T> parent,
            Func<T, IReadOnlyList<T>> children,
            Func<T, bool> usable,
            int max = MaxCandidates)
            where T : class
        {
            var result = new List<Candidate<T>>();
            if (selected == null || !usable(selected)) return result;

            // 房の根元: 親が一本道である限り遡る。
            T top = selected;
            for (T p = parent(top); p != null && usable(p) && UsableChildCount(p, children, usable) == 1; p = parent(p))
                top = p;

            var ends = new List<T>();
            CollectEnds(selected, children, usable, ends, max);

            if (top != selected)
            {
                foreach (T end in ends) Add(result, top, end, max);
                Add(result, top, selected, max);
            }
            foreach (T end in ends) Add(result, selected, end, max);

            return result;
        }

        private static void Add<T>(List<Candidate<T>> result, T start, T end, int max) where T : class
        {
            if (result.Count >= max || start == end) return;
            result.Add(new Candidate<T>(start, end));
        }

        /// <summary>子孫を行きがけ順に辿り、末端と枝分かれ点を集める（node 自身は含めない）。</summary>
        private static void CollectEnds<T>(
            T node, Func<T, IReadOnlyList<T>> children, Func<T, bool> usable, List<T> ends, int max)
            where T : class
        {
            IReadOnlyList<T> list = children(node);
            for (int i = 0; i < list.Count && ends.Count < max; i++)
            {
                T child = list[i];
                if (!usable(child)) continue;

                int count = UsableChildCount(child, children, usable);
                if (count != 1) ends.Add(child);
                CollectEnds(child, children, usable, ends, max);
            }
        }

        private static int UsableChildCount<T>(T node, Func<T, IReadOnlyList<T>> children, Func<T, bool> usable)
        {
            int count = 0;
            foreach (T child in children(node))
                if (usable(child)) count++;
            return count;
        }

        /// <summary>
        /// 複数選んだものを根元から順に並べる。全部が一本の親子の並び
        /// （どれも隣の祖先か子孫）に乗っていなければ false。
        /// 選んだものがそのままハンドルの位置になり、先頭が始点、末尾が終点になる。
        /// </summary>
        internal static bool TryOrderOnLine<T>(IReadOnlyList<T> items, Func<T, T> parent, out List<T> ordered)
            where T : class
        {
            ordered = new List<T>();
            foreach (T item in items)
            {
                if (item != null && !ordered.Contains(item)) ordered.Add(item);
            }
            if (ordered.Count < 2) return false;

            var depth = new Dictionary<T, int>();
            foreach (T item in ordered)
            {
                int d = 0;
                for (T p = parent(item); p != null; p = parent(p)) d++;
                depth[item] = d;
            }
            ordered.Sort((a, b) => depth[a].CompareTo(depth[b]));

            for (int i = 0; i + 1 < ordered.Count; i++)
            {
                if (!IsAncestor(ordered[i], ordered[i + 1], parent)) return false;
            }
            return true;
        }

        private static bool IsAncestor<T>(T ancestor, T node, Func<T, T> parent) where T : class
        {
            for (T p = parent(node); p != null; p = parent(p))
            {
                if (p == ancestor) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------
        // Transform 版
        // ---------------------------------------------------------------

        /// <summary>
        /// Transform の木で候補を並べる。骨でないもの（<see cref="IsBoneLike"/>）は
        /// 辿らない。長さが 0 の鎖も除く。
        /// </summary>
        internal static List<Candidate<Transform>> Find(Transform selected)
        {
            List<Candidate<Transform>> found = Find(
                selected,
                t => t.parent,
                ChildrenOf,
                IsBoneLike);

            var joints = new List<Transform>();
            found.RemoveAll(c =>
                !TransformChain.TryBuild(c.start, c.end, joints, out _) || ChainLength(joints) < ChainSolver.MinBoneLength);
            return found;
        }

        internal static bool TryOrderOnLine(IReadOnlyList<Transform> items, out List<Transform> ordered) =>
            TryOrderOnLine(items, t => t.parent, out ordered);

        internal static float ChainLength(List<Transform> joints)
        {
            float length = 0f;
            for (int i = 1; i < joints.Count; i++)
                length += Vector3.Distance(joints[i - 1].position, joints[i].position);
            return length;
        }

        private static IReadOnlyList<Transform> ChildrenOf(Transform t)
        {
            var list = new Transform[t.childCount];
            for (int i = 0; i < list.Length; i++) list[i] = t.GetChild(i);
            return list;
        }

        /// <summary>
        /// 骨として扱うか。レンダラー付き（メッシュ）は骨ではない。Animator 付きは
        /// アバターのルートなので、房の根元を遡るときにそこを越えて登らないよう止める。
        /// </summary>
        private static bool IsBoneLike(Transform t) =>
            t.GetComponent<Renderer>() == null && t.GetComponent<Animator>() == null;
    }
}
