using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// Unity のギズモにも即時描画にも頼らず、カメラのコマンドバッファへ
    /// メッシュを積んで描く。
    ///
    /// 即時描画（GL / Handles）は 1 発行ごとに SetPass が入り、IMGUI 側では
    /// そのたびに描画をやり直す。Gizmos.* はギズモのコールバックでしか使えず、
    /// 頂点色も持てない。コマンドバッファならどちらの制約も無い:
    ///
    ///   - 自前のマテリアル（Hidden/Internal-Colored）なので頂点色がそのまま出る
    ///   - 積んだ内容はカメラの描画の一部として実行されるので、即時描画にならない
    ///   - 中身が変わらない限り積み直す必要が無い。動かない限り毎フレームの CPU はゼロ
    ///
    /// 実行はカメラの描画時なので、積んだ内容が出るのは次のフレームになる。
    /// </summary>
    internal static class GizmoCommandBuffer
    {
        private const string BufferName = "YozoLab Gizmos";
        private const CameraEvent Event = CameraEvent.AfterEverything;

        // カメラごとに 1 本ずつ持つ
        private static readonly Dictionary<Camera, CommandBuffer> Buffers =
            new Dictionary<Camera, CommandBuffer>();

        private static Material _material;
        private static CompareFunction _zTest = CompareFunction.Always;

        // 積み直しが要るか（中身が変わったか）
        private static int _generation;
        private static readonly Dictionary<Camera, int> Applied = new Dictionary<Camera, int>();

        internal static int LastDrawCallCount { get; private set; }

        /// <summary>中身が変わったので積み直す。</summary>
        internal static void Invalidate() => _generation++;

        internal static Material Material
        {
            get
            {
                if (_material == null)
                {
                    var shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null) return null;

                    _material = new Material(shader)
                    {
                        name = "VRCGizmoAccelerator",
                        hideFlags = HideFlags.HideAndDontSave,
                    };

                    _material.SetInt("_ZWrite", 0);
                    _material.SetInt("_Cull", (int)CullMode.Off);
                    _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                }

                // 奥行き判定は Handles に合わせる
                if (_zTest != Handles.zTest)
                {
                    _zTest = Handles.zTest;
                    _material.SetInt("_ZTest", (int)_zTest);
                }

                return _material;
            }
        }

        /// <summary>
        /// 束ねた 1 枚を積む。
        ///
        /// コマンドバッファはメッシュを参照で持つので、**中身を書き換えても積み直しは
        /// 要らない**。ボーンが動いた場合はメッシュの中身だけ差し替えればよく、
        /// 積み直すのはメッシュそのものが変わったときだけ。
        ///
        /// コンポーネントごとに 1 枚ずつ積む形も試したが、動いたときに
        /// メッシュを個体数ぶん送ることになって逆に遅かった（20 本で +0.16ms）。
        /// </summary>
        internal static void Submit(MeshBuffer triangles, MeshBuffer lines)
        {
            LastDrawCallCount = 0;

            var camera = Camera.current;
            if (camera == null) return;

            var material = Material;
            if (material == null) return;

            LastDrawCallCount = (triangles.VertexCount > 0 ? 1 : 0) + (lines.VertexCount > 0 ? 1 : 0);

            if (Applied.TryGetValue(camera, out int applied) && applied == _generation
                && Buffers.ContainsKey(camera))
            {
                // 既に積んである。カメラが毎フレーム実行してくれる。
                return;
            }

            var buffer = GetOrCreate(camera);
            buffer.Clear();

            var triangleMesh = triangles.GetMesh();
            if (triangleMesh != null) buffer.DrawMesh(triangleMesh, Matrix4x4.identity, material);

            var lineMesh = lines.GetMesh();
            if (lineMesh != null) buffer.DrawMesh(lineMesh, Matrix4x4.identity, material);

            Applied[camera] = _generation;
        }

        private static CommandBuffer GetOrCreate(Camera camera)
        {
            if (Buffers.TryGetValue(camera, out var buffer)) return buffer;

            buffer = new CommandBuffer { name = BufferName };
            camera.AddCommandBuffer(Event, buffer);
            Buffers[camera] = buffer;
            return buffer;
        }

        /// <summary>全て取り外す。設定変更・パッチ解除・ドメインリロード時。</summary>
        internal static void Clear()
        {
            foreach (var pair in Buffers)
            {
                if (pair.Key != null) pair.Key.RemoveCommandBuffer(Event, pair.Value);
                pair.Value.Release();
            }

            Buffers.Clear();
            Applied.Clear();
            _generation++;
        }

        /// <summary>
        /// 描くものが無くなったとき（選択解除など）に、積んである内容を空にする。
        /// 取り外さずに空にするのは、次に描くときの付け直しを省くため。
        /// </summary>
        internal static void ClearContents()
        {
            foreach (var pair in Buffers) pair.Value.Clear();
            Applied.Clear();
            LastDrawCallCount = 0;
        }
    }
}
