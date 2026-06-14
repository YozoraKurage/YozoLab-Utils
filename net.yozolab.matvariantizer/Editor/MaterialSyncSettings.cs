using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.MatVariantizer
{
    /// <summary>
    /// テクスチャ同期モニタの設定。VPM 更新で消えないよう ProjectSettings/ に永続化し、
    /// ドメインリロード・エディタ再起動後も監視登録を保持する。
    /// アバターはシーンをまたいで安定して解決できる GlobalObjectId 文字列で記録する。
    /// </summary>
    [FilePath("ProjectSettings/YozolabMatVariantizerMonitor.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class MaterialSyncSettings : ScriptableSingleton<MaterialSyncSettings>
    {
        public bool enabled = true;
        public List<string> avatarGlobalIds = new List<string>();

        public void Persist() => Save(true);

        public void Add(string id)
        {
            if (string.IsNullOrEmpty(id) || avatarGlobalIds.Contains(id)) return;
            avatarGlobalIds.Add(id);
            Persist();
        }

        public void Remove(string id)
        {
            if (avatarGlobalIds.Remove(id)) Persist();
        }
    }

    /// <summary>GameObject を GlobalObjectId 文字列として記録・復元する。</summary>
    internal static class AvatarRegistry
    {
        internal static string GetId(GameObject go) =>
            go == null ? null : GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();

        internal static GameObject Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!GlobalObjectId.TryParse(id, out GlobalObjectId gid)) return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as GameObject;
        }
    }
}
