using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YozoLab.MatVariantizer
{
    /// <summary>
    /// 現在ロードされているシーンからアバタールートを検出し、
    /// 配下のレンダラー・マテリアルスロットを列挙する。
    ///
    /// VRChat SDK にハード依存しないよう、VRCAvatarDescriptor は
    /// 型名のリフレクションで検出する（SDK 未導入でもコンパイル・動作する）。
    /// </summary>
    internal static class AvatarScanner
    {
        // 新しめの SDK3 Avatars と、念のための短縮名の両方を試す。
        private static readonly string[] DescriptorTypeNames =
        {
            "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor",
            "VRCAvatarDescriptor",
        };

        /// <summary>VRCAvatarDescriptor の型を解決する（無ければ null）。</summary>
        internal static Type ResolveDescriptorType()
        {
            foreach (string name in DescriptorTypeNames)
            {
                Type t = Type.GetType(name);
                if (t != null) return t;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(name);
                    if (t != null) return t;
                }
            }
            return null;
        }

        /// <summary>
        /// ロード済み全シーンから、VRCAvatarDescriptor を持つ GameObject を列挙する。
        /// </summary>
        internal static List<GameObject> FindAvatarRoots()
        {
            var roots = new List<GameObject>();
            Type descriptorType = ResolveDescriptorType();
            if (descriptorType == null) return roots;

            foreach (GameObject sceneRoot in EnumerateSceneRoots())
            {
                foreach (Component comp in sceneRoot.GetComponentsInChildren(descriptorType, true))
                {
                    if (comp != null && !roots.Contains(comp.gameObject))
                        roots.Add(comp.gameObject);
                }
            }
            return roots;
        }

        /// <summary>ロード済み全シーンのルート GameObject を列挙する。</summary>
        internal static IEnumerable<GameObject> EnumerateSceneRoots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    yield return root;
            }
        }

        /// <summary>
        /// アバター配下の MeshRenderer / SkinnedMeshRenderer のマテリアルスロットを列挙する。
        /// null スロットは除外する。
        /// </summary>
        internal static IEnumerable<(Renderer renderer, int slot, Material material)> EnumerateSlots(GameObject avatarRoot)
        {
            foreach (Renderer r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    yield return (r, i, mats[i]);
                }
            }
        }
    }
}
