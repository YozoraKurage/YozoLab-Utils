using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// FBXAnimationExtractorWindow の Rule Detail テンプレート担当。
/// 選択中 Rule の内容を Target Name を除いてコピーし、複数 Rule へ一括ペーストする機能をまとめる。
/// </summary>
public partial class FBXAnimationExtractorWindow
{
    private void CopySelectedRuleToTemplate()
    {
        if (selectedRuleIndex < 0 || selectedRuleIndex >= settings.postProcessRules.Count)
        {
            return;
        }

        serializedSettings.ApplyModifiedProperties();
        AnimationPostProcessRule src = settings.postProcessRules[selectedRuleIndex];
        if (src == null) return;

        ruleTemplate = new RuleDetailTemplate
        {
            sourceTargetName = string.IsNullOrWhiteSpace(src.targetName) ? "(No Target Name)" : src.targetName.Trim(),
            outputDirectoryOverride = src.outputDirectoryOverride,
            useOtherAvatarDefinition = src.useOtherAvatarDefinition,
            avatarDefinition = src.avatarDefinition,
            framesToDelete = src.framesToDelete == null ? new List<int>() : new List<int>(src.framesToDelete),
            shiftToZeroFrame = src.shiftToZeroFrame,
            genericExtract = src.genericExtract,
            genericOutputMode = src.genericOutputMode,
            genericExtractTargets = CopyExtractTargets(src.genericExtractTargets),
            ignoreScaleKey = src.ignoreScaleKey,
            fixScale = src.fixScale,
            fixScaleObjects = src.fixScaleObjects == null ? new List<string>() : new List<string>(src.fixScaleObjects),
            eventMarkers = CopyEventMarkers(src.eventMarkers),
        };
        Debug.Log($"[FBX Animation Extractor] Template copied from \"{ruleTemplate.sourceTargetName}\".");
    }

    private void PasteTemplateToRules(IList<int> ruleIndices)
    {
        if (ruleTemplate == null || ruleIndices == null || ruleIndices.Count == 0) return;

        serializedSettings.ApplyModifiedProperties();
        Undo.RecordObject(settings, "Paste Rule Detail Template");

        int applied = 0;
        foreach (int i in ruleIndices)
        {
            if (i < 0 || i >= settings.postProcessRules.Count) continue;
            AnimationPostProcessRule dst = settings.postProcessRules[i];
            if (dst == null) continue;

            // Target Name は保持
            dst.outputDirectoryOverride = ruleTemplate.outputDirectoryOverride;
            dst.useOtherAvatarDefinition = ruleTemplate.useOtherAvatarDefinition;
            dst.avatarDefinition = ruleTemplate.avatarDefinition;
            dst.framesToDelete = ruleTemplate.framesToDelete == null
                ? new List<int>()
                : new List<int>(ruleTemplate.framesToDelete);
            dst.shiftToZeroFrame = ruleTemplate.shiftToZeroFrame;
            dst.genericExtract = ruleTemplate.genericExtract;
            dst.genericOutputMode = ruleTemplate.genericOutputMode;
            dst.genericExtractTargets = CopyExtractTargets(ruleTemplate.genericExtractTargets);
            dst.ignoreScaleKey = ruleTemplate.ignoreScaleKey;
            dst.fixScale = ruleTemplate.fixScale;
            dst.fixScaleObjects = ruleTemplate.fixScaleObjects == null
                ? new List<string>()
                : new List<string>(ruleTemplate.fixScaleObjects);
            dst.eventMarkers = CopyEventMarkers(ruleTemplate.eventMarkers);
            applied++;
        }

        EditorUtility.SetDirty(settings);
        serializedSettings.Update();
        settings.SaveSettings();
        Debug.Log($"[FBX Animation Extractor] Template pasted to {applied} rule(s).");
    }

    private static List<GenericExtractTargetRule> CopyExtractTargets(List<GenericExtractTargetRule> src)
    {
        var result = new List<GenericExtractTargetRule>();
        if (src == null) return result;
        foreach (GenericExtractTargetRule t in src)
        {
            if (t == null) { result.Add(null); continue; }
            result.Add(new GenericExtractTargetRule
            {
                targetObjectName = t.targetObjectName,
                enableRepath = t.enableRepath,
                repathTo = t.repathTo,
            });
        }
        return result;
    }

    private static List<EventMarkerRule> CopyEventMarkers(List<EventMarkerRule> src)
    {
        var result = new List<EventMarkerRule>();
        if (src == null) return result;
        foreach (EventMarkerRule m in src)
        {
            if (m == null) { result.Add(null); continue; }
            result.Add(new EventMarkerRule
            {
                targetObjectName = m.targetObjectName,
                functionName = m.functionName,
                floatParameter = m.floatParameter,
                intParameter = m.intParameter,
                stringParameter = m.stringParameter,
                objectReferenceParameter = m.objectReferenceParameter,
            });
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Rule Detail Template の保持型
    // ═══════════════════════════════════════════════════════════════
    protected sealed class RuleDetailTemplate
    {
        public string sourceTargetName;
        public DefaultAsset outputDirectoryOverride;
        public bool useOtherAvatarDefinition;
        public Avatar avatarDefinition;
        public List<int> framesToDelete;
        public bool shiftToZeroFrame;
        public bool genericExtract;
        public GenericOutputMode genericOutputMode;
        public List<GenericExtractTargetRule> genericExtractTargets;
        public bool ignoreScaleKey;
        public bool fixScale;
        public List<string> fixScaleObjects;
        public List<EventMarkerRule> eventMarkers;
    }
}
