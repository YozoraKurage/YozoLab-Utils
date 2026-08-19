// VRChat SDK（com.vrchat.base）がある環境でだけコンパイルされる。
// 判定は asmdef の versionDefines に任せてあるので、手動設定は要らない。
#if YOZOLAB_VRCGIZMOACC_VRCSDK
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Dynamics;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 開いているシーン（プレハブステージ含む）にある PhysBone の一覧を持つ。
    ///
    /// 毎フレーム FindObjectsByType するのは避けたいので、構造が変わったかも
    /// しれない合図だけを拾って作り直す。プロパティの変更（radius など）は
    /// 一覧に影響しないため拾わない。
    /// </summary>
    [InitializeOnLoad]
    internal static class PhysBoneScanner
    {
        private static readonly List<VRCPhysBoneBase> Found = new List<VRCPhysBoneBase>();
        private static bool _dirty = true;

        static PhysBoneScanner()
        {
            EditorApplication.hierarchyChanged += MarkDirty;
            Undo.undoRedoPerformed += MarkDirty;
            EditorApplication.playModeStateChanged += _ => MarkDirty();
            EditorSceneManager.sceneOpened += (_, __) => MarkDirty();
            EditorSceneManager.sceneClosed += _ => MarkDirty();
            PrefabStage.prefabStageOpened += _ => MarkDirty();
            PrefabStage.prefabStageClosing += _ => MarkDirty();

            // AddComponent は hierarchyChanged を出さないことがあるので、
            // 構造系の変更イベントも合図にする。
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        internal static void MarkDirty() => _dirty = true;

        internal static IReadOnlyList<VRCPhysBoneBase> All
        {
            get
            {
                if (_dirty)
                {
                    _dirty = false;
                    Found.Clear();
                    Found.AddRange(Object.FindObjectsByType<VRCPhysBoneBase>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                }
                return Found;
            }
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    case ObjectChangeKind.ChangeGameObjectStructure:
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    case ObjectChangeKind.ChangeScene:
                        _dirty = true;
                        return;
                }
            }
        }
    }
}
#endif
