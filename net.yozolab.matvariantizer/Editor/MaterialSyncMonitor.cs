using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.MatVariantizer
{
    /// <summary>
    /// 登録アバターの Variant マテリアルを継続的に監視し、テクスチャの「共有参照」を保つ。
    ///
    /// 差分追従（対称）:
    ///   あるテクスチャスロットが変更されたら、変更直前まで同じテクスチャを指していた
    ///   同一マテリアル内の他スロットも、同じ新しいテクスチャへ自動追従させる。
    ///   （例: _MainTex と輪郭線テクスチャが同じテクスチャを共有 → _MainTex を差し替えると輪郭線も追従）
    ///
    /// 書き込み先は isVariant のマテリアル（＝本ツールが生成する種別）に限定し、原本は編集しない。
    /// ドメインリロード後は現在値を再スナップショットして継続する（過去の差分は伝播しない）。
    /// </summary>
    [InitializeOnLoad]
    internal static class MaterialSyncMonitor
    {
        private const double TickInterval = 0.15;        // ポーリング間隔（秒）
        private const double WatchRefreshInterval = 1.0; // 監視対象の再収集間隔（秒）

        private static readonly Dictionary<Material, Dictionary<string, Texture>> snapshots =
            new Dictionary<Material, Dictionary<string, Texture>>();
        private static readonly List<Material> watched = new List<Material>();
        private static double nextTick;
        private static double nextWatchRefresh;

        static MaterialSyncMonitor()
        {
            EditorApplication.update += Update;
        }

        /// <summary>登録変更時などに、次の tick で監視対象を再収集させる。</summary>
        internal static void InvalidateWatchSet() => nextWatchRefresh = 0;

        private static void Update()
        {
            MaterialSyncSettings settings = MaterialSyncSettings.instance;
            if (!settings.enabled || settings.avatarGlobalIds.Count == 0)
            {
                if (watched.Count > 0) { watched.Clear(); snapshots.Clear(); }
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < nextTick) return;
            nextTick = now + TickInterval;

            if (now >= nextWatchRefresh)
            {
                RefreshWatched(settings);
                nextWatchRefresh = now + WatchRefreshInterval;
            }

            for (int i = 0; i < watched.Count; i++)
                ProcessMaterial(watched[i]);
        }

        private static void RefreshWatched(MaterialSyncSettings settings)
        {
            var current = new HashSet<Material>();
            foreach (string id in settings.avatarGlobalIds)
            {
                GameObject root = AvatarRegistry.Resolve(id);
                if (root == null) continue;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                    foreach (Material m in r.sharedMaterials)
                        if (IsWatchable(m)) current.Add(m);
                }
            }

            watched.Clear();
            foreach (Material m in current)
            {
                watched.Add(m);
                if (!snapshots.ContainsKey(m))
                    snapshots[m] = TakeSnapshot(m); // 初回はスナップショットのみ（伝播しない）
            }

            // 監視対象から外れた / 破棄されたマテリアルのスナップショットを掃除
            var stale = new List<Material>();
            foreach (var kv in snapshots)
                if (kv.Key == null || !current.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (Material m in stale) snapshots.Remove(m);
        }

        private static bool IsWatchable(Material m)
        {
            if (m == null || !m.isVariant) return false;
            string path = AssetDatabase.GetAssetPath(m);
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/");
        }

        private static Dictionary<string, Texture> TakeSnapshot(Material m)
        {
            var snap = new Dictionary<string, Texture>();
            foreach (string prop in m.GetTexturePropertyNames())
                snap[prop] = m.GetTexture(prop);
            return snap;
        }

        private static void ProcessMaterial(Material m)
        {
            if (m == null) return;
            if (!snapshots.TryGetValue(m, out Dictionary<string, Texture> snap))
            {
                snapshots[m] = TakeSnapshot(m);
                return;
            }

            string[] props = m.GetTexturePropertyNames();

            // 変更されたプロパティ（直前値 old と現在値 cur が異なる）を検出
            List<(string prop, Texture old, Texture cur)> changes = null;
            foreach (string prop in props)
            {
                Texture cur = m.GetTexture(prop);
                Texture old = snap.TryGetValue(prop, out Texture v) ? v : cur;
                if (cur != old)
                    (changes ??= new List<(string, Texture, Texture)>()).Add((prop, old, cur));
            }
            if (changes == null) return;

            // 差分追従: 直前に同じ共有元を指していた他スロットを新しい値へ
            bool recorded = false;
            foreach (var (prop, old, cur) in changes)
            {
                if (old == null) continue; // 共有元が無ければ伝播しない
                foreach (string q in props)
                {
                    if (q == prop) continue;
                    Texture qSnap = snap.TryGetValue(q, out Texture sv) ? sv : null;
                    if (qSnap != old) continue;          // 直前に同じ共有元ではない
                    if (m.GetTexture(q) != old) continue; // 既に独立変更済みなら触らない
                    if (!recorded)
                    {
                        Undo.RecordObject(m, "Sync Shared Texture");
                        recorded = true;
                    }
                    m.SetTexture(q, cur);
                }
            }
            if (recorded) EditorUtility.SetDirty(m);

            // スナップショットを最新値へ更新
            foreach (string prop in props)
                snap[prop] = m.GetTexture(prop);
        }
    }
}
