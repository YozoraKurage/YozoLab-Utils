using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// 鎖の途中（または先端）の関節を、指定した位置へ届かせる IK。
    ///
    /// 根元からその関節までの全関節を少しずつ回して届かせる。どの関節をどれだけ
    /// 回すかは重み付きの減衰最小二乗（DLS）で決める。回転の総量が最小になる解を、
    /// 関節ごとの重みで配分し直したもの。
    ///
    /// 重みは「その関節から届かせる関節までの距離」の累乗にする（bias）。
    /// - bias =  0:   根元寄りに曲がる（てこが長いほど効くので、素の最小二乗はこうなる）。
    /// - bias = -0.7: おおむね均等に曲がる。既定。真っ直ぐな 8 本の鎖で先端を横へ
    ///   引いたとき、関節ごとの曲がりが 5〜13 度ほどに収まる値として選んだ。
    /// - bias = -1.5: 先端寄りに曲がる。
    ///
    /// 届かせる関節より先は、親の回転にそのまま付いていく（形を保って運ばれる）。
    /// 位置は回転の結果としてしか動かないので、ボーンの長さは変わらない。
    /// 届かない位置なら、届く範囲でできるだけ近づいたところで止まる。
    /// </summary>
    internal static class ChainIK
    {
        internal const float DefaultBias = -0.7f;

        private const int MaxIterations = 100;

        /// <summary>1 回の反復で 1 関節が回ってよい最大角（ラジアン）。大きく跳ねないように。</summary>
        private const float MaxStep = 0.2f;

        /// <summary>
        /// positions / rotations（世界座標・世界回転）をその場で解き進める。
        /// 前回の解を渡せば続きから解くので、ドラッグ中に形が飛ばない。
        /// </summary>
        /// <param name="effector">届かせる関節の番号（first より後）。</param>
        /// <param name="target">届かせたい世界座標。</param>
        /// <param name="first">
        /// 回してよい最初の関節。これより根元側は動かさない（途中から先だけを解くとき）。
        /// </param>
        /// <returns>最後に残った誤差（距離）。</returns>
        internal static float Solve(
            Vector3[] positions, Quaternion[] rotations, int effector, Vector3 target,
            float bias = DefaultBias, int first = 0)
        {
            int count = positions.Length;
            if (first < 0 || effector <= first || effector >= count) return 0f;

            float reach = 0f;
            for (int i = first; i < effector; i++) reach += Vector3.Distance(positions[i], positions[i + 1]);
            if (reach < ChainSolver.MinBoneLength) return Vector3.Distance(positions[effector], target);

            float tolerance = reach * 1e-5f;
            var omega = new Vector3[effector];
            float error = Vector3.Distance(positions[effector], target);
            bool nudged = false;

            for (int iteration = 0; iteration < MaxIterations && error > tolerance; iteration++)
            {
                Vector3 tip = positions[effector];
                Vector3 e = target - tip;

                // A = Σ w (|d|² I - d dᵀ)。先端の動き = A λ、各関節の回転 ω = w (d × λ)。
                float a00 = 0f, a01 = 0f, a02 = 0f, a11 = 0f, a12 = 0f, a22 = 0f;
                var weights = new float[effector];
                for (int i = first; i < effector; i++)
                {
                    Vector3 d = tip - positions[i];
                    float len = d.magnitude;
                    if (len < ChainSolver.MinBoneLength) continue;

                    float w = Mathf.Pow(len, bias);
                    weights[i] = w;
                    float l2 = len * len;
                    a00 += w * (l2 - d.x * d.x);
                    a11 += w * (l2 - d.y * d.y);
                    a22 += w * (l2 - d.z * d.z);
                    a01 -= w * d.x * d.y;
                    a02 -= w * d.x * d.z;
                    a12 -= w * d.y * d.z;
                }

                // 届かない向き（鎖が真っ直ぐ伸び切っている等）で特異にならないよう、少し減衰を足す。
                float damping = 1e-3f * (a00 + a11 + a22) / 3f + 1e-12f;
                if (!Solve3(a00 + damping, a01, a02, a11 + damping, a12, a22 + damping, e, out Vector3 lambda)) break;

                float largest = 0f;
                for (int i = first; i < effector; i++)
                {
                    Vector3 d = tip - positions[i];
                    omega[i] = weights[i] * Vector3.Cross(d, lambda);
                    largest = Mathf.Max(largest, omega[i].magnitude);
                }

                if (largest < 1e-7f)
                {
                    // 真っ直ぐな鎖を、軸に沿って縮める向きに引いたとき。どの関節を回しても
                    // 1 次では先端が動かないので、真ん中を少しだけ折って足がかりにする。
                    if (nudged) break;
                    nudged = true;
                    Nudge(positions, rotations, first, effector);
                    error = Vector3.Distance(positions[effector], target);
                    continue;
                }

                float scale = largest > MaxStep ? MaxStep / largest : 1f;

                // 先端側から順に回す。根元側の回転は、先に回した子孫ごと運ぶ。
                for (int i = effector - 1; i >= first; i--)
                {
                    Vector3 w = omega[i] * scale;
                    float angle = w.magnitude;
                    if (angle < 1e-9f) continue;
                    RotateSubtree(positions, rotations, i, Quaternion.AngleAxis(angle * Mathf.Rad2Deg, w / angle));
                }

                float next = Vector3.Distance(positions[effector], target);
                if (next > error - tolerance * 1e-3f && scale >= 1f && iteration > 8)
                {
                    error = next;
                    break; // もう縮まない（届かない位置で落ち着いた）
                }
                error = next;
            }

            return error;
        }

        /// <summary>
        /// 途中のハンドル（handles[active]）を掴んだときの IK。
        ///
        /// 根元から掴んだ関節までは <see cref="Solve"/> で届かせる。その先は、先にある
        /// ハンドルを 1 区間ずつ元の位置（pinPositions）へ届かせ直し、最後に先端の関節を
        /// 元の向き（pinRotations）へ戻す。掴んだ所から離れた部分ほど今の位置と姿勢に
        /// 留まり、曲がりは掴んだ所の前後に集まる。
        ///
        /// 区間の長さが足りずに元の位置へ届かない場合は、届く範囲で近づいたところで止まる
        /// （長さは変えない）。先端のハンドルを掴んだときは <see cref="Solve"/> と同じ。
        /// </summary>
        /// <param name="handles">ハンドルが乗っている関節の番号（根元 0 から先端まで昇順）。</param>
        internal static void SolveKeepingRest(
            Vector3[] positions, Quaternion[] rotations, int[] handles, int active, Vector3 target,
            Vector3[] pinPositions, Quaternion[] pinRotations, float bias = DefaultBias)
        {
            Solve(positions, rotations, handles[active], target, bias);

            int last = handles.Length - 1;
            if (active >= last) return;

            for (int m = active + 1; m <= last; m++)
            {
                int pin = handles[m];
                Solve(positions, rotations, pin, pinPositions[pin], bias, handles[m - 1]);
            }

            // 先端の関節は向きも元に戻す。鎖の外（先端の子）はこれに付いてくる。
            int tip = handles[last];
            rotations[tip] = pinRotations[tip];
        }

        /// <summary>関節 index を中心に、それ以降すべてを q だけ回す（関節 index 自身の回転も）。</summary>
        internal static void RotateSubtree(Vector3[] positions, Quaternion[] rotations, int index, Quaternion q)
        {
            Vector3 pivot = positions[index];
            rotations[index] = q * rotations[index];
            for (int j = index + 1; j < positions.Length; j++)
            {
                positions[j] = pivot + q * (positions[j] - pivot);
                rotations[j] = q * rotations[j];
            }
        }

        private static void Nudge(Vector3[] positions, Quaternion[] rotations, int first, int effector)
        {
            int middle = (first + effector) / 2;
            Vector3 bone = positions[middle + 1] - positions[middle];
            Vector3 axis = Vector3.Cross(bone, Vector3.up);
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(bone, Vector3.right);
            if (axis.sqrMagnitude < 1e-10f) return;
            RotateSubtree(positions, rotations, middle, Quaternion.AngleAxis(2f, axis.normalized));
        }

        /// <summary>対称 3x3 の連立一次方程式。</summary>
        private static bool Solve3(float a00, float a01, float a02, float a11, float a12, float a22, Vector3 b, out Vector3 x)
        {
            float c00 = a11 * a22 - a12 * a12;
            float c01 = a02 * a12 - a01 * a22;
            float c02 = a01 * a12 - a02 * a11;
            float det = a00 * c00 + a01 * c01 + a02 * c02;
            if (Mathf.Abs(det) < 1e-30f)
            {
                x = Vector3.zero;
                return false;
            }

            float c11 = a00 * a22 - a02 * a02;
            float c12 = a01 * a02 - a00 * a12;
            float c22 = a00 * a11 - a01 * a01;
            float inv = 1f / det;
            x = new Vector3(
                (c00 * b.x + c01 * b.y + c02 * b.z) * inv,
                (c01 * b.x + c11 * b.y + c12 * b.z) * inv,
                (c02 * b.x + c12 * b.y + c22 * b.z) * inv);
            return true;
        }
    }
}
