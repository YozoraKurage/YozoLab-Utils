using UnityEngine;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 溜めた図形を Unity のギズモレンダラへ渡す。
    ///
    /// Gizmos.* はギズモのコールバックの中でしか使えない代わりに、即時描画では
    /// なく Unity 自身のギズモ描画に乗る。IMGUI（UIToolkit）は即時描画が挟まる
    /// たびに溜めていたものを吐き出してレンダーターゲットを貼り直すので、
    /// そこを通らないだけで負荷が変わる。
    ///
    /// 線は色ごとに持ち替えながら渡す。塗り（三角形）はメッシュ 1 枚で渡すが、
    /// Gizmos は頂点色を持てないので代表色 1 色になる。
    /// </summary>
    internal static class GizmoSubmitter
    {
        internal static int LastDrawCallCount { get; private set; }

        /// <summary>
        /// この経路が使えるか。一度でも失敗したら二度と使わず即時描画に任せる。
        /// ギズモの描画で例外を漏らすと GUI の状態（GUIClip）まで壊れるので、
        /// 疑わしい場合は使わないほうがよい。
        /// </summary>
        internal static bool Available { get; private set; } = true;

        internal static void Submit(MeshBuffer triangles, MeshBuffer lines)
        {
            LastDrawCallCount = 0;
            if (!Available) return;

            var previousColor = Gizmos.color;
            var previousMatrix = Gizmos.matrix;

            // 頂点はワールド座標で持っているので変換は要らない
            Gizmos.matrix = Matrix4x4.identity;

            try
            {
                SubmitTriangles(triangles);
                SubmitLines(lines);
            }
            catch (System.Exception e)
            {
                // ここで外へ投げると、呼び出し元の GUI 処理ごと壊れる
                Available = false;
                Debug.LogWarning(
                    "[VRC Gizmo Accelerator] Gizmos 経由の描画が使えなかったため、"
                    + $"以後は即時描画に戻します: {e.Message}");
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
            }
        }

        /// <summary>設定を変えたときなどに、もう一度試せるようにする。</summary>
        internal static void ResetAvailability() => Available = true;

        private static void SubmitLines(MeshBuffer buffer)
        {
            var vertices = buffer.RawVertices;
            var colors = buffer.RawColors;
            int count = buffer.VertexCount;
            if (count < 2) return;

            bool colorSet = false;
            var current = default(Color);

            for (int i = 0; i + 1 < count; i += 2)
            {
                if (!colorSet || colors[i] != current)
                {
                    current = colors[i];
                    Gizmos.color = current;
                    colorSet = true;
                }

                Gizmos.DrawLine(vertices[i], vertices[i + 1]);
            }

            LastDrawCallCount += count / 2;
        }

        private static void SubmitTriangles(MeshBuffer buffer)
        {
            // Gizmos.DrawMesh は法線の無いメッシュを受け付けない
            var mesh = buffer.GetMesh(withNormals: true);
            if (mesh == null) return;

            // Gizmos は頂点色を持てないので代表色を 1 つ選ぶ。
            // 塗りは同系色でまとめて描かれるので、実用上はこれで足りる。
            Gizmos.color = buffer.RawColors[0];
            Gizmos.DrawMesh(mesh);
            LastDrawCallCount++;
        }
    }
}
