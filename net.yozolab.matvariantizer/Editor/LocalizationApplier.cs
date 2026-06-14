using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YozoLab.MatVariantizer
{
    /// <summary>
    /// 計画の適用をオーケストレーションする。
    /// 生成（Variant / テクスチャコピー）→ テクスチャ差し替え → 参照差し替えの順に実行し、
    /// シーン参照の変更は Prefab インスタンスのオーバーライドとして記録する。
    /// </summary>
    internal static class LocalizationApplier
    {
        internal sealed class Report
        {
            public int variantsCreated;
            public int variantsReused;
            public int texturesCreated;
            public int texturesReused;
            public int slotsRebound;
            public readonly List<string> warnings = new List<string>();
        }

        internal static Report Apply(LocalizationPlan plan, bool includeTextures)
        {
            var report = new Report();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Material Variantize");

            try
            {
                foreach (AvatarGroup avatar in plan.avatars)
                {
                    CreateVariants(avatar, report);
                    if (includeTextures)
                    {
                        CreateTextureCopies(avatar, report);
                        RebindVariantTextures(avatar);
                    }
                    RebindSlots(avatar, report);
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            AssetDatabase.Refresh();
            return report;
        }

        // 1) 選択マテリアル → Variant 生成/再利用
        private static void CreateVariants(AvatarGroup avatar, Report report)
        {
            foreach (MaterialPlan mp in avatar.materials)
            {
                if (!mp.selected) continue;
                mp.createdVariant = VariantFactory.GetOrCreateVariant(mp.original, mp.variantPath, out bool created);
                if (mp.createdVariant == null)
                {
                    report.warnings.Add($"{avatar.name}: '{Name(mp.original)}' の Variant 生成に失敗しました。");
                    continue;
                }
                if (created) report.variantsCreated++;
                else report.variantsReused++;
            }
        }

        // 2) 選択テクスチャ → アバター内 1 コピーに集約生成/再利用
        private static void CreateTextureCopies(AvatarGroup avatar, Report report)
        {
            foreach (TexturePlan tp in avatar.textures)
            {
                if (!tp.selected || !tp.Actionable) continue;
                tp.createdCopy = TextureLocalizer.GetOrCreateCopy(tp.originalTexture, tp.copyPath, out bool created);
                if (tp.createdCopy == null)
                {
                    report.warnings.Add($"{avatar.name}: テクスチャ '{Name(tp.originalTexture)}' の複製に失敗しました。");
                    continue;
                }
                if (created) report.texturesCreated++;
                else report.texturesReused++;
            }
        }

        // 3) Variant のテクスチャプロパティをローカルコピーへ（同一原本の全参照を同時更新）
        private static void RebindVariantTextures(AvatarGroup avatar)
        {
            foreach (TexturePlan tp in avatar.textures)
            {
                if (!tp.selected || tp.createdCopy == null) continue;
                foreach (TextureRef r in tp.references)
                {
                    Material variant = r.material.createdVariant;
                    if (variant == null) continue; // 親マテリアル未選択（通常 NormalizeSelection で発生しない）
                    if (!variant.HasProperty(r.property)) continue;
                    variant.SetTexture(r.property, tp.createdCopy);
                    EditorUtility.SetDirty(variant);
                }
            }
        }

        // 4) レンダラースロット → Variant（Prefab インスタンスのオーバーライドとして記録）
        private static void RebindSlots(AvatarGroup avatar, Report report)
        {
            foreach (MaterialPlan mp in avatar.materials)
            {
                if (!mp.selected || mp.createdVariant == null) continue;
                foreach (MaterialSlotRef s in mp.slots)
                {
                    if (RebindSlot(s, mp.createdVariant)) report.slotsRebound++;
                }
            }
        }

        private static bool RebindSlot(MaterialSlotRef slot, Material variant)
        {
            Renderer r = slot.renderer;
            if (r == null) return false;

            var so = new SerializedObject(r);
            SerializedProperty mats = so.FindProperty("m_Materials");
            if (mats == null || slot.slotIndex >= mats.arraySize) return false;

            SerializedProperty element = mats.GetArrayElementAtIndex(slot.slotIndex);
            if (element.objectReferenceValue == variant) return false; // 既に一致

            element.objectReferenceValue = variant;
            so.ApplyModifiedProperties(); // Undo 登録 + Prefab インスタンスではオーバーライドとして記録

            if (PrefabUtility.IsPartOfPrefabInstance(r))
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            return true;
        }

        private static string Name(Object o) => o != null ? o.name : "(null)";
    }
}
