using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>始点から終点までの Transform の並びを作る。</summary>
    internal static class TransformChain
    {
        /// <summary>
        /// 終点から親を辿って始点に着いたら、その道のりを根元から順に joints へ入れる。
        /// 途中の Transform はすべて鎖の関節になる（飛ばすと親の回転で子がずれるため）。
        /// </summary>
        internal static bool TryBuild(Transform start, Transform end, List<Transform> joints, out string error)
        {
            joints.Clear();
            error = null;

            if (start == null || end == null)
            {
                error = "始点と終点を指定してください。";
                return false;
            }
            if (start == end)
            {
                error = "始点と終点が同じです。";
                return false;
            }

            for (Transform t = end; t != null; t = t.parent)
            {
                joints.Add(t);
                if (t == start)
                {
                    joints.Reverse();
                    return true;
                }
            }

            joints.Clear();
            error = "終点が始点の子孫ではありません。";
            return false;
        }

        /// <summary>
        /// 選ばれている Transform を全部。Selection.transforms は親が選ばれている子を
        /// 落とす（トップレベルだけ返す）ので、親子を 2 つ選んだ場合に使えない。
        /// </summary>
        internal static Transform[] SelectedTransforms() =>
            Selection.GetFiltered<Transform>(SelectionMode.Unfiltered);

        /// <summary>親子関係にある 2 つから、親側を始点、子側を終点にする。</summary>
        internal static bool TryGuess(IReadOnlyList<Transform> selected, out Transform start, out Transform end)
        {
            start = end = null;
            if (selected == null || selected.Count != 2) return false;

            Transform a = selected[0], b = selected[1];
            if (a == null || b == null || a == b) return false;
            if (b.IsChildOf(a)) { start = a; end = b; return true; }
            if (a.IsChildOf(b)) { start = b; end = a; return true; }
            return false;
        }

        /// <summary>鎖のどこかに非一様スケールがあるか。</summary>
        internal static bool HasNonUniformScale(List<Transform> joints)
        {
            foreach (Transform t in joints)
            {
                Vector3 s = t.lossyScale;
                float max = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                float min = Mathf.Min(Mathf.Abs(s.x), Mathf.Min(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                if (max > 0f && (max - min) / max > 1e-3f) return true;
            }
            return false;
        }
    }
}
