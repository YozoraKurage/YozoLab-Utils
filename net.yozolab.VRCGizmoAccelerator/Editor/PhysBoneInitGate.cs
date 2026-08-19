// VRChat SDK（com.vrchat.base）がある環境でだけコンパイルされる。
#if YOZOLAB_VRCGIZMOACC_VRCSDK
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// InitTransforms（ボーン構造の作り直し）の門番。
    ///
    /// force 付きの InitTransforms は PhysBone 1 本ごとに
    /// transform.root.GetComponentsInChildren でアバター全体を走査する。
    /// 1 つのオブジェクトに PhysBone が複数載っていると、組み立てのたびに
    /// その本数ぶん全走査が走り、組み立て時間が本数に比例して膨らむ。
    ///
    /// 一方、代替パスの頂点は毎回 bone.transform.position を直接読むので、
    /// ボーンが「動くだけ」なら作り直しは要らない。構成が変わるのは階層か
    /// プロパティが変わったときだけ。そこで変更イベントを版数にまとめ、
    /// 版が変わった最初の 1 回だけ force で作り直し、それ以外は force=false
    /// （SDK 内の hasInitTransform で即 return）に落とす。
    /// </summary>
    [InitializeOnLoad]
    internal static class PhysBoneInitGate
    {
        private static int _version = 1;
        private static readonly Dictionary<int, int> Initialized = new Dictionary<int, int>();

        static PhysBoneInitGate()
        {
            EditorApplication.hierarchyChanged += Bump;
            Undo.undoRedoPerformed += Bump;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorApplication.playModeStateChanged += _ => Bump();
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            // 何が変わったかまでは見ない。作り直しは「変わったフレームに 1 回」で
            // 済むので、「何か変わった」だけで十分。
            if (stream.length > 0) Bump();
        }

        internal static void Bump()
        {
            _version++;
            if (Initialized.Count > 4096) Initialized.Clear();
        }

        /// <summary>
        /// ボーン構造を最新にする。この版で作り直し済みなら何もしない
        /// （再生中は SDK と同じく常に force しない）。
        /// </summary>
        internal static void EnsureInitialized(VRCPhysBoneBase pb)
        {
            int id = pb.GetInstanceID();
            bool stale = !Initialized.TryGetValue(id, out int version) || version != _version;

            pb.InitTransforms(stale && !Application.isPlaying);
            if (stale) Initialized[id] = _version;
        }
    }
}
#endif
