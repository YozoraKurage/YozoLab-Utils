using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// FBXAnimationBakerWindow のエントリ設定テンプレート担当。
    /// 選択中エントリのベイク設定を「そのエントリが何であるか」(名前 / Source FBX /
    /// クリップ / 所属フォルダ)を除いてコピーし、複数エントリへ一括ペーストする。
    ///
    /// 同じベイク設定を何十本ものクリップへ揃えるのが実際の使い方なので、
    /// エントリを 1 つ作り込んでから残りへ配る流れになる。
    /// </summary>
    public partial class FBXAnimationBakerWindow
    {
        private void CopySelectedEntryToTemplate()
        {
            if (settings.bakeEntries == null
                || selectedEntryIndex < 0 || selectedEntryIndex >= settings.bakeEntries.Count)
            {
                return;
            }

            serializedSettings.ApplyModifiedProperties();
            AnimationBakeEntry src = settings.bakeEntries[selectedEntryIndex];
            if (src == null) return;

            entryTemplate = new EntryDetailTemplate
            {
                sourceEntryName = GetEntryDisplayName(src),
                outputDirectoryOverride = src.outputDirectoryOverride,
                useOtherAvatarDefinition = src.useOtherAvatarDefinition,
                avatarDefinition = src.avatarDefinition,
                frameRate = src.frameRate,
                bakeRootMotion = src.bakeRootMotion,
                bakeScale = src.bakeScale,
                bakeBlendShapes = src.bakeBlendShapes,
                excludeBlendShapes = src.excludeBlendShapes,
                removeConstantCurves = src.removeConstantCurves,
                keyframeReduction = src.keyframeReduction,
                reductionTolerance = src.reductionTolerance,
                exportContent = src.exportContent,
                saveBakedClipAsset = src.saveBakedClipAsset,
                fastImport = src.fastImport,
                importAnimationType = src.importAnimationType,
                exportAscii = src.exportAscii,
            };
            Debug.Log($"{LogPrefix} Template copied from \"{entryTemplate.sourceEntryName}\".");
        }

        private void PasteTemplateToEntries(IList<int> entryIndices)
        {
            if (entryTemplate == null || entryIndices == null || entryIndices.Count == 0) return;

            serializedSettings.ApplyModifiedProperties();
            Undo.RecordObject(settings, "Paste Entry Template");

            int applied = 0;
            foreach (int i in entryIndices)
            {
                if (i < 0 || i >= settings.bakeEntries.Count) continue;
                AnimationBakeEntry dst = settings.bakeEntries[i];
                if (dst == null) continue;

                // 名前 / Source FBX / クリップ / 所属フォルダ / 実行フラグ は保持。
                // そこはエントリの identity であって、設定ではない。
                dst.outputDirectoryOverride = entryTemplate.outputDirectoryOverride;
                dst.useOtherAvatarDefinition = entryTemplate.useOtherAvatarDefinition;
                dst.avatarDefinition = entryTemplate.avatarDefinition;
                dst.frameRate = entryTemplate.frameRate;
                dst.bakeRootMotion = entryTemplate.bakeRootMotion;
                dst.bakeScale = entryTemplate.bakeScale;
                dst.bakeBlendShapes = entryTemplate.bakeBlendShapes;
                dst.excludeBlendShapes = entryTemplate.excludeBlendShapes;
                dst.removeConstantCurves = entryTemplate.removeConstantCurves;
                dst.keyframeReduction = entryTemplate.keyframeReduction;
                dst.reductionTolerance = entryTemplate.reductionTolerance;
                dst.exportContent = entryTemplate.exportContent;
                dst.saveBakedClipAsset = entryTemplate.saveBakedClipAsset;
                dst.fastImport = entryTemplate.fastImport;
                dst.importAnimationType = entryTemplate.importAnimationType;
                dst.exportAscii = entryTemplate.exportAscii;
                applied++;
            }

            EditorUtility.SetDirty(settings);
            serializedSettings.Update();
            settings.SaveSettings();
            Debug.Log($"{LogPrefix} Template pasted to {applied} entry/entries.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  エントリ設定テンプレートの保持型
        // ═══════════════════════════════════════════════════════════════
        protected sealed class EntryDetailTemplate
        {
            public string sourceEntryName;
            public DefaultAsset outputDirectoryOverride;
            public bool useOtherAvatarDefinition;
            public Avatar avatarDefinition;
            public float frameRate;
            public bool bakeRootMotion;
            public bool bakeScale;
            public bool bakeBlendShapes;
            public bool excludeBlendShapes;
            public bool removeConstantCurves;
            public bool keyframeReduction;
            public float reductionTolerance;
            public BakeExportContent exportContent;
            public bool saveBakedClipAsset;
            public bool fastImport;
            public BakedFbxAnimationType importAnimationType;
            public bool exportAscii;
        }
    }
}
