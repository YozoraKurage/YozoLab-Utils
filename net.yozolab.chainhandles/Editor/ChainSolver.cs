using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// 鎖の解き方。Unity のオブジェクトには触れず、世界座標の点と回転だけを扱う。
    ///
    /// 考え方はカーブデフォーマ + 長さの拘束。
    ///
    /// 1. 掴む前の鎖（rest）に沿って、弧長で等間隔に制御点を置く。制御点を通る
    ///    Catmull-Rom 曲線が「鎖の芯」になる。
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
        /// 制御点を置く。鎖を弧長で divisions 等分した divisions + 1 点（根元と先端を含む）。
        /// </summary>
        internal static Vector3[] PlaceControls(IReadOnlyList<Vector3> joints, int divisions)
        {
            divisions = Mathf.Max(1, divisions);
            float[] parameters = JointParameters(joints, out _);
            var controls = new Vector3[divisions + 1];
            for (int k = 0; k <= divisions; k++)
            {
                controls[k] = parameters == null
                    ? joints[0]
                    : PointOnChain(joints, parameters, (float)k / divisions);
            }
            return controls;
        }

        // ---------------------------------------------------------------
        // 芯の曲線
        // ---------------------------------------------------------------

        /// <summary>
        /// 制御点を通る Catmull-Rom 曲線上の点。u は 0〜1 で、制御点 k が u = k / (数 - 1)。
        /// 両端は外側に鏡映した仮想点で延ばす（端で曲線が暴れないように）。
        /// </summary>
        internal static Vector3 Spline(IReadOnlyList<Vector3> c, float u)
        {
            GetSegment(c, u, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3, out float t);
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (
                p0 * (-t3 + 2f * t2 - t) +
                p1 * (3f * t3 - 5f * t2 + 2f) +
                p2 * (-3f * t3 + 4f * t2 + t) +
                p3 * (t3 - t2));
        }

        /// <summary>曲線の接線（正規化していない。u に対する微分）。</summary>
        internal static Vector3 SplineTangent(IReadOnlyList<Vector3> c, float u)
        {
            GetSegment(c, u, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3, out float t);
            float t2 = t * t;
            return 0.5f * (
                p0 * (-3f * t2 + 4f * t - 1f) +
                p1 * (9f * t2 - 10f * t) +
                p2 * (-9f * t2 + 8f * t + 1f) +
                p3 * (3f * t2 - 2f * t));
        }

        private static void GetSegment(
            IReadOnlyList<Vector3> c, float u,
            out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3, out float t)
        {
            int segments = c.Count - 1;
            if (segments <= 0)
            {
                p0 = p1 = p2 = p3 = c[0];
                t = 0f;
                return;
            }

            float x = Mathf.Clamp01(u) * segments;
            int s = Mathf.Min((int)x, segments - 1);
            t = x - s;

            p1 = c[s];
            p2 = c[s + 1];
            p0 = s > 0 ? c[s - 1] : 2f * p1 - p2;
            p3 = s + 2 <= segments ? c[s + 2] : 2f * p2 - p1;
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
            Quaternion[] rotations)
        {
            int jointCount = restPositions.Count;
            for (int i = 0; i < jointCount; i++)
            {
                positions[i] = restPositions[i];
                rotations[i] = restRotations[i];
            }

            if (jointCount < 2 || controls.Count < 2 || controls.Count != restControls.Count) return false;

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
                Vector3 restTangent = SafeDirection(SplineTangent(restControls, u), restTangentPrev);
                Vector3 tangent = SafeDirection(SplineTangent(controls, u), tangentPrev);

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

                float roll = SampleRoll(rolls, u);
                deform[j] = Quaternion.AngleAxis(roll, tangent) * toNow;

                // rest の鎖の点を、rest の芯からのずれごと今の芯へ運ぶ。
                Vector3 restOnChain = PointOnChain(restPositions, jointU, u);
                Vector3 offset = restOnChain - Spline(restControls, u);
                target[j] = Spline(controls, u) + deform[j] * offset;
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

        /// <summary>ロールの線形補間。制御点 k が u = k / (数 - 1)。</summary>
        internal static float SampleRoll(IReadOnlyList<float> rolls, float u)
        {
            if (rolls == null || rolls.Count == 0) return 0f;
            int segments = rolls.Count - 1;
            if (segments == 0) return rolls[0];

            float x = Mathf.Clamp01(u) * segments;
            int s = Mathf.Min((int)x, segments - 1);
            return Mathf.Lerp(rolls[s], rolls[s + 1], x - s);
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
