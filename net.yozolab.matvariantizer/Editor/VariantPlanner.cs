using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YozoLab.MatVariantizer
{
    /// <summary>
    /// スキャン結果から <see cref="LocalizationPlan"/> を構築する。
    /// マテリアルはアバター×真の原本ごとに 1 つの Variant に、
    /// テクスチャはアバター×原本ごとに 1 つのコピーに集約する。
    /// アセットへの書き込みは行わない（状態判定と生成先パスの算出のみ）。
    /// </summary>
    internal static class VariantPlanner
    {
        internal static LocalizationPlan Build(
            IEnumerable<GameObject> avatarRoots, string baseFolder, bool includeTextures)
        {
            var plan = new LocalizationPlan { baseFolder = NormalizePath(baseFolder) };

            foreach (GameObject root in avatarRoots)
            {
                if (root == null) continue;

                var avatar = new AvatarGroup { root = root, name = root.name };
                avatar.folder = $"{plan.baseFolder}/{SanitizeName(root.name)}";

                BuildMaterialPlans(avatar);
                if (includeTextures) BuildTexturePlans(avatar);

                plan.avatars.Add(avatar);
            }

            NormalizeSelection(plan);
            return plan;
        }

        private static void BuildMaterialPlans(AvatarGroup avatar)
        {
            var byOriginal = new Dictionary<Material, MaterialPlan>();
            var claimedPaths = new Dictionary<string, Material>();

            foreach (var (renderer, slot, referenced) in AvatarScanner.EnumerateSlots(avatar.root))
            {
                // 参照が既に自フォルダの Variant なら、真の原本はその parent。
                bool ourVariant = IsOurVariant(referenced, avatar.folder);
                Material trueOriginal = ourVariant && referenced.parent != null
                    ? referenced.parent : referenced;

                if (!byOriginal.TryGetValue(trueOriginal, out MaterialPlan mp))
                {
                    mp = new MaterialPlan { avatar = avatar, original = trueOriginal };
                    mp.variantPath = ResolveVariantPath(avatar.folder, trueOriginal, claimedPaths);
                    mp.existingVariant = AssetDatabase.LoadAssetAtPath<Material>(mp.variantPath);
                    mp.status = (mp.existingVariant != null && mp.existingVariant.parent == trueOriginal)
                        ? MaterialRowStatus.ReuseExisting
                        : MaterialRowStatus.New;
                    mp.selected = mp.status == MaterialRowStatus.New;
                    avatar.materials.Add(mp);
                    byOriginal[trueOriginal] = mp;
                }

                mp.slots.Add(new MaterialSlotRef
                {
                    renderer = renderer,
                    slotIndex = slot,
                    inPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(renderer),
                });
            }

            // 全スロットが既に Variant を指していれば「適用済み」（既定で選択解除）。
            foreach (MaterialPlan mp in avatar.materials)
            {
                if (mp.existingVariant == null) continue;
                bool allBound = true;
                foreach (MaterialSlotRef s in mp.slots)
                {
                    Material[] mats = s.renderer.sharedMaterials;
                    if (s.slotIndex >= mats.Length || mats[s.slotIndex] != mp.existingVariant)
                    {
                        allBound = false;
                        break;
                    }
                }
                mp.alreadyBound = allBound;
                mp.selected = !allBound; // 未バインドのスロットが残っていれば既定で対象にする
            }
        }

        private static void BuildTexturePlans(AvatarGroup avatar)
        {
            var byOriginal = new Dictionary<Texture, TexturePlan>();
            var claimedPaths = new Dictionary<string, Texture>();
            string texFolder = $"{avatar.folder}/Textures";

            foreach (MaterialPlan mp in avatar.materials)
            {
                // 既存 Variant があればその現状から、無ければ原本から読む。
                Material source = mp.existingVariant != null ? mp.existingVariant : mp.original;
                Material shaderRef = mp.original != null ? mp.original : source;
                if (source == null || shaderRef == null) continue;

                foreach (string prop in shaderRef.GetTexturePropertyNames())
                {
                    if (!source.HasProperty(prop)) continue;
                    Texture tex = source.GetTexture(prop);
                    if (tex == null) continue;

                    string texPath = AssetDatabase.GetAssetPath(tex);
                    if (string.IsNullOrEmpty(texPath)) continue; // 実体無し（生成テクスチャ等）

                    if (!byOriginal.TryGetValue(tex, out TexturePlan tp))
                    {
                        tp = new TexturePlan { avatar = avatar, originalTexture = tex };
                        tp.status = ClassifyTexture(tex, texPath, texFolder, claimedPaths, out string copyPath);
                        tp.copyPath = copyPath;
                        avatar.textures.Add(tp);
                        byOriginal[tex] = tp;
                    }

                    tp.references.Add(new TextureRef { material = mp, property = prop });
                    mp.textureProps.Add(new TextureProp { property = prop, texture = tp });
                }
            }
        }

        private static TextureRowStatus ClassifyTexture(
            Texture tex, string texPath, string texFolder,
            Dictionary<string, Texture> claimed, out string copyPath)
        {
            copyPath = null;

            // 既に自フォルダのコピーを参照している → 対象外。
            if (NormalizePath(texPath).StartsWith(texFolder + "/"))
                return TextureRowStatus.AlreadyLocalized;

            // FBX 等への埋め込み（サブアセット）は CopyAsset できない。
            if (AssetDatabase.IsSubAsset(tex) || !AssetDatabase.IsMainAsset(tex))
                return TextureRowStatus.Embedded;

            copyPath = ResolveTexturePath(texFolder, tex, texPath, claimed);
            return AssetDatabase.LoadAssetAtPath<Texture>(copyPath) != null
                ? TextureRowStatus.ReuseExisting
                : TextureRowStatus.New;
        }

        /// <summary>
        /// テクスチャ選択は「参照する全マテリアルが選択済み」を不変条件とする。
        /// これにより同一原本テクスチャの全参照が必ず同時に更新される。
        /// </summary>
        internal static void NormalizeSelection(LocalizationPlan plan)
        {
            foreach (AvatarGroup avatar in plan.avatars)
            {
                foreach (TexturePlan tp in avatar.textures)
                {
                    if (!tp.selected) continue;
                    foreach (TextureRef r in tp.references)
                    {
                        if (!r.material.selected)
                        {
                            tp.selected = false;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// テクスチャの選択を切り替える。ON にする場合は参照する全マテリアルも選択する。
        /// </summary>
        internal static void SelectTexture(TexturePlan tp, bool on)
        {
            tp.selected = on && tp.Actionable;
            if (!tp.selected) return;
            foreach (TextureRef r in tp.references)
                r.material.selected = true;
        }

        // ---- パス解決 ----

        private static string ResolveVariantPath(
            string folder, Material original, Dictionary<string, Material> claimed)
        {
            string baseName = SanitizeName(original != null ? original.name : "Material");
            for (int serial = 1; ; serial++)
            {
                string candidate = serial == 1
                    ? $"{folder}/{baseName}.mat"
                    : $"{folder}/{baseName}_{serial}.mat";

                if (claimed.TryGetValue(candidate, out Material owner))
                {
                    if (owner == original) return candidate;
                    continue; // この実行内で別原本が占有済み
                }

                Material existing = AssetDatabase.LoadAssetAtPath<Material>(candidate);
                if (existing == null || existing.parent == original)
                {
                    claimed[candidate] = original; // 空き or 自分の Variant → 確定
                    return candidate;
                }
                claimed[candidate] = existing; // 別原本の Variant が占有 → 次のシリアルへ
            }
        }

        private static string ResolveTexturePath(
            string folder, Texture tex, string srcPath, Dictionary<string, Texture> claimed)
        {
            string ext = Path.GetExtension(srcPath); // ドット込み
            string baseName = SanitizeName(Path.GetFileNameWithoutExtension(srcPath));
            for (int serial = 1; ; serial++)
            {
                string candidate = serial == 1
                    ? $"{folder}/{baseName}{ext}"
                    : $"{folder}/{baseName}_{serial}{ext}";

                if (claimed.TryGetValue(candidate, out Texture owner))
                {
                    if (owner == tex) return candidate;
                    continue;
                }
                claimed[candidate] = tex;
                return candidate;
            }
        }

        // ---- ユーティリティ ----

        internal static string NormalizePath(string p) =>
            string.IsNullOrEmpty(p) ? p : p.Replace('\\', '/').TrimEnd('/');

        private static bool IsOurVariant(Material m, string avatarFolder)
        {
            if (m == null || !m.isVariant) return false;
            string path = NormalizePath(AssetDatabase.GetAssetPath(m));
            return !string.IsNullOrEmpty(path) && path.StartsWith(avatarFolder + "/");
        }

        /// <summary>アセットパスに使える文字だけに整形する。</summary>
        internal static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unnamed";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' ? c : '_');
            string result = sb.ToString().Trim();
            return result.Length > 0 ? result : "Unnamed";
        }
    }
}
