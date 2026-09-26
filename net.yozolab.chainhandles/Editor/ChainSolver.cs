using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// 鎖の解き方。Unity のオブジェクトには触れず、世界座標の点と回転だけを扱う。
    ///
    /// 考え方はカーブデフォーマ + 長さの拘束。
    ///
    /// 1. 掴む前の鎖（rest）に沿って、弧長で等間隔に（または指定した関節に）制御点を
    ///    置く。制御点を通る曲線が「鎖の芯」になる。
    /// 2. 制御点を動かすと芯が曲がる。芯に沿って平行移動させた座標系（+ ロール）で
    ///    rest の鎖を芯ごと運び、「鎖が行きたい場所」の曲線を作る。
    ///    芯から外れていた分（うねりなど）も一緒に曲がって付いてくる。
    /// 3. その曲線を根元から辿り、各ボーンの長さちょうどの位置に次の関節を置く。
    ///    ここで長さが確定するので、曲線がどれだけ伸び縮みしても鎖は伸び縮みしない。
    ///    曲線が鎖より短ければ末端の向きのまま先へ延ばし、長ければ届かずに終わる。
    /// 4. 各関節の回転は「芯の座標系で運んだ rest の回転」に、子関節を 3 の位置へ
    ///    向けるための小さな補正を掛けたもの。
    ///
    /// 出力は回転だけで、位置は結果として付いてくる。呼び出し側も回転しか
    /// 書き込まないので、localPosition / localScale は変わらない。
    ///
    /// 制御点を動かさなければ 2 の曲線は rest の鎖そのものになり、3 は rest の
    /// 関節をそのまま拾う。掴んだ瞬間に形が跳ねることはない。
    /// </summary>
    internal static class ChainSolver
    {
        /// <summary>これより短いボーンは向きを持たないものとして扱う。</summary>
        internal const float MinBoneLength = 1e-6f;

        /// <summary>鎖全体での曲線のサンプル数の下限。関節の位置は別途必ず含める。</summary>
        private const int MinSamples = 64;

        /// <summary>ボーン 1 本あたりのサンプル数。</summary>
        private const int SamplesPerBone = 8;

        /// <summary>鎖の各関節の弧長パラメータ（根元 0、先端 1）。全長が 0 なら null。</summary>
        internal static float[] JointParameters(IReadOnlyList<Vector3> joints, out float totalLength)
        {
            int count = joints.Count;
            var cumulative = new float[count];
            for (int i = 1; i < count; i++)
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(joints[i - 1], joints[i]);

            totalLength = count > 0 ? cumulative[count - 1] : 0f;
            if (totalLength < MinBoneLength) return null;

            for (int i = 0; i < count; i++) cumulative[i] /= totalLength;
            cumulative[count - 1] = 1f;
            return cumulative;
        }

        /// <summary>鎖の折れ線上で、弧長パラメータ u の点。</summary>
        internal static Vector3 PointOnChain(IReadOnlyList<Vector3> joints, float[] parameters, float u)
        {
            int last = joints.Count - 1;
            if (u <= 0f) return joints[0];
            if (u >= 1f) return joints[last];

            for (int i = 0; i < last; i++)
            {
                float a = parameters[i], b = parameters[i + 1];
                if (u > b) continue;
                float t = b - a > 0f ? (u - a) / (b - a) : 0f;
                return Vector3.LerpUnclamped(joints[i], joints[i + 1], t);
            }
            return joints[last];
        }

        /// <summary>
        /// 鎖をおおよそ弧長で divisions 等分する位置に近い関節を選ぶ。ハンドルは必ず関節の上に置く
        /// （掴んだハンドルがどのボーンを動かすのかをはっきりさせるため）。
        /// 分割数はボーンの本数までに抑え、同じ関節を二度選ばないようにする。
        /// 根元（0）と先端（最後の関節）は必ず含む。
        /// </summary>
        internal static int[] EvenJointIndices(IReadOnlyList<Vector3> joints, int divisions)
        {
            int last = joints.Count - 1;
            if (last < 1) return new[] { 0 };

            divisions = Mathf.Clamp(divisions, 1, last);
            float[] parameters = JointParameters(joints, out _);
            var indices = new int[divisions + 1];

            for (int k = 0; k <= divisions; k++)
            {
                float u = (float)k / divisions;
                int best = 0;
                if (parameters != null)
                {
                    for (int i = 1; i <= last; i++)
                    {
                        if (Mathf.Abs(parameters[i] - u) < Mathf.Abs(parameters[best] - u)) best = i;
                    }
                }
                else
                {
                    best = Mathf.RoundToInt(u * last);
                }

                // 前のハンドルより先、かつ残りのハンドルが入る余地を残す。
                int low = k == 0 ? 0 : indices[k - 1] + 1;
                int high = last - (divisions - k);
                indices[k] = Mathf.Clamp(best, low, high);
            }
            return indices;
        }

        /// <summary>分割数で関節を選んで制御点を置く。<see cref="EvenJointIndices"/> を参照。</summary>
        internal static Vector3[] PlaceControls(IReadOnlyList<Vector3> joints, int divisions, out float[] knots) =>
            PlaceControls(joints, EvenJointIndices(joints, divisions), out knots, out _);

        internal static Vector3[] PlaceControls(
            IReadOnlyList<Vector3> joints, IReadOnlyList<int> indices, out float[] knots) =>
            PlaceControls(joints, indices, out knots, out _);

        /// <summary>
        /// 指定した関節に制御点を置く。
        /// indices は根元 0 から先端までの昇順で、先頭は 0、末尾は最後の関節であること。
        /// 同じ位置に重なる関節（長さ 0 のボーン越し）は 1 つにまとめる。
        /// </summary>
        /// <param name="knots">各制御点の弧長パラメータ。<see cref="Solve"/> へそのまま渡す。</param>
        /// <param name="kept">実際に制御点を置いた関節の番号（まとめた後）。</param>
        internal static Vector3[] PlaceControls(
            IReadOnlyList<Vector3> joints, IReadOnlyList<int> indices, out float[] knots, out int[] kept)
        {
            float[] parameters = JointParameters(joints, out _);
            var controls = new List<Vector3>();
            var knotList = new List<float>();
            var keptList = new List<int>();

            if (parameters != null)
            {
                foreach (int index in indices)
                {
                    float u = parameters[index];
                    if (knotList.Count > 0 && u - knotList[knotList.Count - 1] < 1e-6f) continue;
                    controls.Add(joints[index]);
                    knotList.Add(u);
                    keptList.Add(index);
                }
            }

            knots = knotList.ToArray();
            kept = keptList.ToArray();
            return controls.ToArray();
        }

        // ---------------------------------------------------------------
        // 芯の曲線
        // ---------------------------------------------------------------

        /// <summary>
        /// 制御点を通る曲線上の点。u は 0〜1。
        ///
        /// 制御点 k の位置 u は knots[k]。knots が null なら等間隔（k / (数 - 1)）で、
        /// そのときは一様な Catmull-Rom と一致する。間隔が不揃いでも曲線が暴れないよう、
        /// 各制御点の接線は前後の制御点の差をパラメータの差で割ったもの（Hermite）にする。
        /// 両端は外側に鏡映した仮想点で延ばす。
        /// </summary>
        internal static Vector3 Spline(IReadOnlyList<Vector3> c, float u, IReadOnlyList<float> knots = null)
        {
            GetSegment(c, knots, u, out Vector3 p1, out Vector3 p2, out Vector3 m1, out Vector3 m2, out float t, out _);
            float t2 = t * t, t3 = t2 * t;
            return
                p1 * (2f * t3 - 3f * t2 + 1f) +
                m1 * (t3 - 2f * t2 + t) +
                p2 * (-2f * t3 + 3f * t2) +
                m2 * (t3 - t2);
        }

        /// <summary>曲線の接線（正規化していない。u に対する微分）。</summary>
        internal static Vector3 SplineTangent(IReadOnlyList<Vector3> c, float u, IReadOnlyList<float> knots = null)
        {
            GetSegment(c, knots, u, out Vector3 p1, out Vector3 p2, out Vector3 m1, out Vector3 m2, out float t, out float h);
            float t2 = t * t;
            Vector3 dt =
                p1 * (6f * t2 - 6f * t) +
                m1 * (3f * t2 - 4f * t + 1f) +
                p2 * (-6f * t2 + 6f * t) +
                m2 * (3f * t2 - 2f * t);
            return h > 0f ? dt / h : dt;
        }

        /// <summary>制御点 k の弧長パラメータ。</summary>
        internal static float Knot(IReadOnlyList<float> knots, int count, int k) =>
            knots != null ? knots[k] : count > 1 ? (float)k / (count - 1) : 0f;

        /// <summary>
        /// u を含む区間の両端の点と、区間の長さ h を掛けた接線（m1, m2）。t は区間内の 0〜1。
        /// </summary>
        private static void GetSegment(
            IReadOnlyList<Vector3> c, IReadOnlyList<float> knots, float u,
            out Vector3 p1, out Vector3 p2, out Vector3 m1, out Vector3 m2, out float t, out float h)
        {
            int count = c.Count;
            int segments = count - 1;
            if (segments <= 0)
            {
                p1 = p2 = c[0];
                m1 = m2 = Vector3.zero;
                t = h = 0f;
                return;
            }

            u = Mathf.Clamp01(u);
            int s = 0;
            while (s < segments - 1 && u > Knot(knots, count, s + 1)) s++;

            float u1 = Knot(knots, count, s);
            float u2 = Knot(knots, count, s + 1);
            h = u2 - u1;
            t = h > 0f ? Mathf.Clamp01((u - u1) / h) : 0f;

            p1 = c[s];
            p2 = c[s + 1];
            m1 = h * PointTangent(c, knots, s);
            m2 = h * PointTangent(c, knots, s + 1);
        }

        /// <summary>
        /// 制御点 k での接線（u に対する微分）。前後の点の差をパラメータの差で割る。
        /// 端は鏡映した仮想点を置いたのと同じで、隣との差をその間隔で割ったものになる。
        /// </summary>
        private static Vector3 PointTangent(IReadOnlyList<Vector3> c, IReadOnlyList<float> knots, int k)
        {
            int count = c.Count;
            int prev = Mathf.Max(k - 1, 0);
            int next = Mathf.Min(k + 1, count - 1);
            float du = Knot(knots, count, next) - Knot(knots, count, prev);
            return du > 0f ? (c[next] - c[prev]) / du : Vector3.zero;
        }

        // ---------------------------------------------------------------
        // 解く
        // ---------------------------------------------------------------

        /// <summary>
        /// 鎖を解く。
        /// </summary>
        /// <param name="restPositions">掴む前の関節の世界座標（根元から先端へ）。</param>
        /// <param name="restRotations">掴む前の関節の世界回転。</param>
        /// <param name="restControls">掴む前の制御点（<see cref="PlaceControls"/> の結果）。</param>
        /// <param name="controls">動かした後の制御点。数は restControls と同じ。</param>
        /// <param name="rolls">制御点ごとのロール（度、芯まわり）。間は線形に補間する。</param>
        /// <param name="knots">制御点ごとの弧長パラメータ。null なら等間隔。</param>
        /// <param name="positions">解いた関節の世界座標（確認用。書き込むのは回転だけでよい）。</param>
        /// <param name="rotations">解いた関節の世界回転。</param>
        /// <returns>解けたか。鎖の全長が 0 などで解けなければ false（出力は rest のまま）。</returns>
        internal static bool Solve(
            IReadOnlyList<Vector3> restPositions,
            IReadOnlyList<Quaternion> restRotations,
            IReadOnlyList<Vector3> restControls,
            IReadOnlyList<Vector3> controls,
            IReadOnlyList<float> rolls,
            Vector3[] positions,
            Quaternion[] rotations,
            IReadOnlyList<float> knots = null)
        {
            int jointCount = restPositions.Count;
            for (int i = 0; i < jointCount; i++)
            {
                positions[i] = restPositions[i];
                rotations[i] = restRotations[i];
            }

            if (jointCount < 2 || controls.Count < 2 || controls.Count != restControls.Count) return false;
            if (knots != null && knots.Count != controls.Count) return false;

            float[] jointU = JointParameters(restPositions, out _);
            if (jointU == null) return false;

            // --- サンプル位置: 等間隔 + 各関節の位置（関節の角を削らないため） ---
            List<float> samples = BuildSamples(jointU);
            int n = samples.Count;

            // --- 芯に沿った座標系の移り変わり（rest → 今） ---
            var target = new Vector3[n];
            var deform = new Quaternion[n];

            Vector3 restTangentPrev = Vector3.zero, tangentPrev = Vector3.zero;
            Quaternion restFrame = Quaternion.identity;
            Quaternion frame = Quaternion.identity;

            for (int j = 0; j < n; j++)
            {
                float u = samples[j];
                Vector3 restTangent = SafeDirection(SplineTangent(restControls, u, knots), restTangentPrev);
                Vector3 tangent = SafeDirection(SplineTangent(controls, u, knots), tangentPrev);

                if (j == 0)
                {
                    // 根元: rest の接線を今の接線へ最短で回したものを起点にする。
                    frame = FromTo(restTangent, tangent);
                }
                else
                {
                    // 平行移動（ねじれを足さない最小回転）で運ぶ。
                    restFrame = FromTo(restTangentPrev, restTangent) * restFrame;
                    frame = FromTo(tangentPrev, tangent) * frame;
                }

                restTangentPrev = restTangent;
                tangentPrev = tangent;

                Quaternion toNow = FrameDelta(frame, restFrame);

                float roll = SampleRoll(rolls, u, knots);
                deform[j] = Quaternion.AngleAxis(roll, tangent) * toNow;

                // rest の鎖の点を、rest の芯からのずれごと今の芯へ運ぶ。
                Vector3 restOnChain = PointOnChain(restPositions, jointU, u);
                Vector3 offset = restOnChain - Spline(restControls, u, knots);
                target[j] = Spline(controls, u, knots) + deform[j] * offset;
            }

            // --- 行き先の曲線を根元から辿り、長さを保って関節を置く ---
            positions[0] = restPositions[0];
            int segment = 0;
            for (int i = 0; i + 1 < jointCount; i++)
            {
                float length = Vector3.Distance(restPositions[i], restPositions[i + 1]);
                positions[i + 1] = length < MinBoneLength
                    ? positions[i]
                    : Walk(target, positions[i], length, ref segment);
            }

            // --- 回転 ---
            int sampleIndex = 0;
            for (int i = 0; i < jointCount; i++)
            {
                // jointU の値はそのまま samples に入っているので、前から探せば必ず当たる。
                while (sampleIndex < n - 1 && samples[sampleIndex] < jointU[i]) sampleIndex++;
                Quaternion carried = deform[sampleIndex] * restRotations[i];

                if (i + 1 < jointCount)
                {
                    Vector3 restBone = restPositions[i + 1] - restPositions[i];
                    Vector3 bone = positions[i + 1] - positions[i];
                    if (restBone.sqrMagnitude >= MinBoneLength * MinBoneLength &&
                        bone.sqrMagnitude >= MinBoneLength * MinBoneLength)
                    {
                        Vector3 carriedBone = deform[sampleIndex] * restBone;
                        carried = FromTo(carriedBone, bone) * carried;
                    }
                }

                rotations[i] = Normalize(carried);
            }

            return true;
        }

        /// <summary>
        /// その地点での rest → 今の回転。
        ///
        /// restFrame は恒等から始めて rest の接線に沿って運んだもの（根元の rest 接線を
        /// その地点の rest 接線へ移す）。frame は根元で「rest 接線 → 今の接線」から始めて
        /// 今の接線に沿って運んだもの（根元の rest 接線をその地点の今の接線へ移す）。
        /// よって frame * restFrame^-1 がその地点の rest 接線を今の接線へ移し、
        /// ねじれは平行移動の分しか入らない。
        /// </summary>
        private static Quaternion FrameDelta(Quaternion frame, Quaternion restFrame) =>
            Normalize(frame * Quaternion.Inverse(restFrame));

        private static List<float> BuildSamples(float[] jointU)
        {
            int uniform = Mathf.Max(MinSamples, (jointU.Length - 1) * SamplesPerBone);
            var samples = new List<float>(uniform + jointU.Length + 1);
            for (int j = 0; j <= uniform; j++) samples.Add((float)j / uniform);
            samples.AddRange(jointU);
            samples.Sort();

            // 重複を除く。関節の u は後で完全一致で探すので、関節側の値を残す。
            var unique = new List<float>(samples.Count);
            foreach (float u in samples)
            {
                if (unique.Count > 0 && u - unique[unique.Count - 1] < 1e-6f)
                {
                    if (System.Array.IndexOf(jointU, u) >= 0) unique[unique.Count - 1] = u;
                    continue;
                }
                unique.Add(u);
            }
            return unique;
        }

        /// <summary>ロールの線形補間。制御点 k の位置は knots[k]（null なら等間隔）。</summary>
        internal static float SampleRoll(IReadOnlyList<float> rolls, float u, IReadOnlyList<float> knots = null)
        {
            if (rolls == null || rolls.Count == 0) return 0f;
            int count = rolls.Count;
            if (count == 1) return rolls[0];

            u = Mathf.Clamp01(u);
            int s = 0;
            while (s < count - 2 && u > Knot(knots, count, s + 1)) s++;

            float u1 = Knot(knots, count, s), u2 = Knot(knots, count, s + 1);
            float t = u2 - u1 > 0f ? Mathf.Clamp01((u - u1) / (u2 - u1)) : 0f;
            return Mathf.Lerp(rolls[s], rolls[s + 1], t);
        }

        /// <summary>
        /// 折れ線 points を segment 番目の辺から先へ辿り、from からちょうど length 離れる
        /// 最初の点を返す。見つからなければ末端の向きのまま先へ延ばす。
        /// </summary>
        internal static Vector3 Walk(Vector3[] points, Vector3 from, float length, ref int segment)
        {
            int last = points.Length - 1;
            for (; segment < last; segment++)
            {
                Vector3 a = points[segment], b = points[segment + 1];
                if ((b - from).sqrMagnitude < length * length) continue;
                if (TryExitSphere(a, b - a, from, length, out float t))
                    return a + (b - a) * Mathf.Clamp01(t);
                return b;
            }

            // 曲線が鎖より短い。最後の辺の向きのまま延ばす。
            segment = last;
            Vector3 end = points[last];
            Vector3 direction = last > 0 ? points[last] - points[last - 1] : Vector3.zero;
            for (int k = last - 1; direction.sqrMagnitude < 1e-12f && k > 0; k--)
                direction = points[last] - points[k - 1];
            if (direction.sqrMagnitude < 1e-12f) direction = end - from;
            if (direction.sqrMagnitude < 1e-12f) direction = Vector3.up;
            direction.Normalize();

            return TryExitSphere(end, direction, from, length, out float s)
                ? end + direction * Mathf.Max(0f, s)
                : from + direction * length;
        }

        /// <summary>直線 a + d t が中心 c 半径 r の球を出ていくときの t（大きい方の解）。</summary>
        private static bool TryExitSphere(Vector3 a, Vector3 d, Vector3 c, float r, out float t)
        {
            Vector3 f = a - c;
            float qa = Vector3.Dot(d, d);
            float qb = 2f * Vector3.Dot(f, d);
            float qc = Vector3.Dot(f, f) - r * r;
            float disc = qb * qb - 4f * qa * qc;
            if (qa < 1e-12f || disc < 0f)
            {
                t = 0f;
                return false;
            }
            t = (-qb + Mathf.Sqrt(disc)) / (2f * qa);
            return true;
        }

        // ---------------------------------------------------------------
        // 小物
        // ---------------------------------------------------------------

        private static Vector3 SafeDirection(Vector3 v, Vector3 fallback)
        {
            float m = v.magnitude;
            if (m > 1e-6f) return v / m;
            return fallback.sqrMagnitude > 0f ? fallback : Vector3.forward;
        }

        /// <summary>
        /// Quaternion.FromToRotation は正反対のときに軸を適当に選ぶ。
        /// 芯が折り返すような極端な操作でも破綻しないよう、ここに寄せておく。
        /// </summary>
        private static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            if (from.sqrMagnitude < 1e-12f || to.sqrMagnitude < 1e-12f) return Quaternion.identity;
            return Quaternion.FromToRotation(from, to);
        }

        private static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 1e-12f) return Quaternion.identity;
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }
    }
}
