using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// 鎖を「どのアバターのものか」でまとめるための小物。
    ///
    /// アバターのルートは、VRChat SDK があれば VRCAvatarDescriptor の付いたオブジェクト。
    /// SDK が無い環境では一番上の Animator、それも無ければ階層のルートをアバターとみなす。
    /// SDK の型は名前で探すので、SDK への参照は持たない（無くてもコンパイルが通る）。
    ///
    /// アバターの識別には GlobalObjectId（シーン + オブジェクト）を使う。シーンを開き直しても
    /// 同じアバターを指し、同じモデルを複製した別のアバターとは区別される。
    /// 鎖の関節はアバターのルートからの相対パスで持つ。
    /// </summary>
    [InitializeOnLoad]
    internal static class AvatarRoots
    {
        private const string DescriptorTypeName = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";

        private static Type _descriptorType;
        private static bool _lookedUp;

        /// <summary>識別子 → ルート。見つからなかったものも null で覚えておく。</summary>
        private static readonly Dictionary<string, Transform> Resolved = new Dictionary<string, Transform>();

        static AvatarRoots()
        {
            // 階層が変わったら引き直す（アバターの追加・削除、シーンの開閉、Undo）。
            EditorApplication.hierarchyChanged += Invalidate;
            EditorSceneManager.sceneOpened += (_, __) => Invalidate();
            EditorSceneManager.sceneClosed += _ => Invalidate();
            Undo.undoRedoPerformed += Invalidate;
        }

        internal static void Invalidate() => Resolved.Clear();

        private static Type DescriptorType
        {
            get
            {
                if (_lookedUp) return _descriptorType;
                _lookedUp = true;
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = assembly.GetType(DescriptorTypeName, false);
                    if (type == null) continue;
                    _descriptorType = type;
                    break;
                }
                return _descriptorType;
            }
        }

        /// <summary>VRChat SDK（アバター）が入っているか。</summary>
        internal static bool HasVrcSdk => DescriptorType != null;

        /// <summary>t が属するアバターのルート。</summary>
        internal static Transform FindRoot(Transform t)
        {
            if (t == null) return null;

            Type descriptor = DescriptorType;
            if (descriptor != null)
            {
                for (Transform p = t; p != null; p = p.parent)
                {
                    if (p.GetComponent(descriptor) != null) return p;
                }
            }

            Transform topAnimator = null;
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.GetComponent<Animator>() != null) topAnimator = p;
            }
            return topAnimator != null ? topAnimator : t.root;
        }

        internal static string KeyOf(Transform root) =>
            GlobalObjectId.GetGlobalObjectIdSlow(root.gameObject).ToString();

        /// <summary>識別子から今シーンにあるアバターのルートを引く。無ければ null。</summary>
        internal static Transform Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            // 「見つからなかった」(本物の null) はそのまま返し、破棄済みのものだけ引き直す。
            if (Resolved.TryGetValue(key, out Transform cached) && (ReferenceEquals(cached, null) || cached != null))
                return cached;

            Transform found = null;
            if (GlobalObjectId.TryParse(key, out GlobalObjectId id) &&
                GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is GameObject go)
            {
                found = go.transform;
            }
            Resolved[key] = found;
            return found;
        }

        // ---------------------------------------------------------------
        // 相対パス
        // ---------------------------------------------------------------

        /// <summary>root から t までのパス（"Armature/Hips/..."）。t が root なら ""、root の外なら null。</summary>
        internal static string PathFrom(Transform root, Transform t)
        {
            if (root == null || t == null) return null;
            if (t == root) return string.Empty;

            var names = new List<string>();
            for (Transform p = t; p != root; p = p.parent)
            {
                if (p == null) return null;
                names.Add(p.name);
            }
            names.Reverse();

            var builder = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) builder.Append('/');
                builder.Append(names[i]);
            }
            return builder.ToString();
        }

        /// <summary><see cref="PathFrom"/> の逆。同名の兄弟がいれば先頭を選ぶ。</summary>
        internal static Transform Find(Transform root, string path)
        {
            if (root == null || path == null) return null;
            if (path.Length == 0) return root;

            Transform current = root;
            foreach (string name in path.Split('/'))
            {
                Transform next = null;
                for (int i = 0; i < current.childCount; i++)
                {
                    Transform child = current.GetChild(i);
                    if (child.name != name) continue;
                    next = child;
                    break;
                }
                if (next == null) return null;
                current = next;
            }
            return current;
        }
    }
}
