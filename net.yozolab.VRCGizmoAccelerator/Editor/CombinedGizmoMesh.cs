using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// コンポーネントごとのキャッシュを 1 枚のメッシュに束ねて、
    /// 1 リペイントにつき 1 回だけ発行する。
    ///
    /// 即時描画は 1 発行ごとに SetPass が入り、UIToolkit のレンダラはそのたびに
    /// 溜めていたものを吐き出してレンダーターゲットを貼り直す。線 1 本ごとの発行は
    /// 潰したが、コンポーネントごとの発行はまだ残っていた（PhysBone 30 本なら
    /// 30 回/リペイント = 実測 3,000 回/秒）。ここまで束ねればリペイントに 1 回になる。
    ///
    /// 束ね直しは中身のコピーだけなので、どれかが動いた場合でも安い。
    /// </summary>
    internal static class CombinedGizmoMesh
    {
        private static readonly MeshBuffer Lines = new MeshBuffer(MeshTopology.Lines);
        private static readonly MeshBuffer Triangles = new MeshBuffer(MeshTopology.Triangles);

        private static bool _dirty = true;
        private static bool _hasContent;

        internal static int LastDrawCallCount { get; private set; }
        internal static int VertexCount => Lines.VertexCount + Triangles.VertexCount;

        /// <summary>束ね直しが要る（誰かが作り直された / 顔ぶれが変わった）。</summary>
        internal static void Invalidate() => _dirty = true;

        internal static bool NeedsRebuild => _dirty;

        internal static void Clear()
        {
            Lines.Release();
            Triangles.Release();
            _dirty = true;
            _hasContent = false;
            GizmoCommandBuffer.Clear();
        }

        /// <summary>キャッシュ済みのものを順に詰め直す。</summary>
        internal static void Rebuild(List<GizmoGeometryCache.Entry> entries)
        {
            Lines.Clear();
            Triangles.Clear();

            foreach (var entry in entries)
            {
                Lines.Append(entry.Lines);
                Triangles.Append(entry.Triangles);
            }

            _dirty = false;
            _hasContent = VertexCount > 0;

            // 積んであるコマンドバッファはこのメッシュを参照している。
            // 中身を今のうちに送っておけば、積み直さずに新しい形で描かれる。
            if (VRCGizmoAcceleratorSettings.instance.drawMode == GizmoDrawMode.CommandBuffer)
            {
                Triangles.GetMesh();
                Lines.GetMesh();
            }

        }

        /// <summary>束ねたものを描く。SetPass は 1 回だけ。</summary>
        internal static void Draw()
        {
            LastDrawCallCount = 0;
            if (!_hasContent) return;

            switch (VRCGizmoAcceleratorSettings.instance.drawMode)
            {
                case GizmoDrawMode.CommandBuffer:
                    GizmoCommandBuffer.Submit(Triangles, Lines);
                    LastDrawCallCount = GizmoCommandBuffer.LastDrawCallCount;
                    break;

                case GizmoDrawMode.GizmoLines:
                    GizmoSubmitter.Submit(Triangles, Lines);
                    LastDrawCallCount = GizmoSubmitter.LastDrawCallCount;
                    break;

                default:
                    GizmoBatch.DrawBuffers(Triangles, Lines);
                    LastDrawCallCount = GizmoBatch.LastDrawCallCount;
                    break;
            }
        }

    }
}
