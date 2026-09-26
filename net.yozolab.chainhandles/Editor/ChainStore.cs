using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.ChainHandles
{
    internal enum SolveMode
    {
        IK,
        Curve,
    }

    /// <summary>
    /// 作った鎖と設定の保存先。アバターごとに鎖をまとめて持つ。
    ///
    /// 置き場はプロジェクトの UserSettings/。アバターやシーン、アセットには何も書かない
    /// （アップロードされる側を汚さない）。UserSettings はユーザーごとの設定置き場で、
    /// 普通はバージョン管理にも入らない。
    ///
    /// 関節はアバターのルートからの相対パスで持つので、シーンを開き直しても、
    /// アバターが一時的にシーンから消えても、戻ってくればそのまま使える。
    /// </summary>
    [FilePath("UserSettings/YozoLab.ChainHandles.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ChainStore : ScriptableSingleton<ChainStore>
    {
        [Serializable]
        internal sealed class Chain
        {
            /// <summary>選択やドラッグ中の対象を指すための識別子（Undo で中身が作り直されても変わらない）。</summary>
            public string id = Guid.NewGuid().ToString("N");

            public string start;
            public string end;

            /// <summary>ハンドルを置くボーンのパス。空なら分割数で選ぶ。</summary>
            public List<string> anchors = new List<string>();

            public int divisions = 3;
            public bool visible = true;
        }

        [Serializable]
        internal sealed class AvatarEntry
        {
            /// <summary><see cref="AvatarRoots.KeyOf"/>。</summary>
            public string key;

            /// <summary>シーンに無いときに一覧へ出す名前（最後に見たときの名前）。</summary>
            public string name;

            public bool visible = true;
            public bool expanded = true;
            public List<Chain> chains = new List<Chain>();
        }

        public List<AvatarEntry> avatars = new List<AvatarEntry>();

        public bool gizmosVisible = true;
        public SolveMode mode = SolveMode.IK;
        public float ikBias = ChainIK.DefaultBias;

        /// <summary>IK で途中のハンドルを掴んだとき、それより先をなるべく今の位置・姿勢に留める。</summary>
        public bool keepRest = true;
        public bool carryDownstream;
        public float handleScale = 1f;

        /// <summary>中身が変わった（編集・Undo）。表示を更新したい側が購読する。</summary>
        internal static event Action Changed;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            // Undo でメモリ上の中身は戻るが、ファイルへは書かれないので書き直す。
            Save(true);
            Changed?.Invoke();
        }

        /// <summary>Undo に記録してから変更し、保存する。</summary>
        internal void Modify(string undoName, Action change)
        {
            Undo.RecordObject(this, undoName);
            change();
            Commit();
        }

        /// <summary>
        /// Undo に記録せずに変更し、保存する。表示の ON/OFF のような「見え方」の切り替え用
        /// （Undo の履歴に積むと、戻したい編集の間に挟まって邪魔になる）。
        /// </summary>
        internal void Set(Action change)
        {
            change();
            Commit();
        }

        internal void Commit()
        {
            Save(true);
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        internal AvatarEntry Find(string key) => avatars.Find(a => a.key == key);

        /// <summary>このアバターの欄。無ければ作る（Undo 記録は呼び出し側で）。</summary>
        internal AvatarEntry GetOrAdd(Transform root)
        {
            string key = AvatarRoots.KeyOf(root);
            AvatarEntry entry = Find(key);
            if (entry == null)
            {
                entry = new AvatarEntry { key = key };
                avatars.Add(entry);
            }
            entry.name = root.name;
            return entry;
        }

        internal bool TryFindChain(string id, out AvatarEntry avatar, out Chain chain)
        {
            foreach (AvatarEntry a in avatars)
            {
                foreach (Chain c in a.chains)
                {
                    if (c.id != id) continue;
                    avatar = a;
                    chain = c;
                    return true;
                }
            }
            avatar = null;
            chain = null;
            return false;
        }
    }
}
