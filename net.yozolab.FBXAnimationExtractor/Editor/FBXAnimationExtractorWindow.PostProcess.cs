using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// FBXAnimationExtractorWindow のカーブ後処理・Generic抽出・差分シグネチャ担当。
/// 抽出済みクリップへ Rule を適用する純粋な変換ロジックをここにまとめる。
/// </summary>
public partial class FBXAnimationExtractorWindow
{
    private void ApplyPostProcessRules(AnimationClip clip, string clipName, string sourceFbxPath)
    {
        AnimationPostProcessRule matchingRule = FindMatchingRule(clipName);

        if (matchingRule == null)
            return;

        if (matchingRule.genericExtract)
        {
            MergeGenericTransformAnimation(clip, sourceFbxPath, matchingRule.genericExtractTargets, matchingRule.fixScale, matchingRule.fixScaleObjects, matchingRule.ignoreScaleKey);
        }

        Debug.Log($"[FBX Animation Extractor] Applying post-process rule: {matchingRule.targetName} -> {clipName}");

        // フレームレートを取得
        float frameRate = clip.frameRate;
        if (frameRate <= 0) frameRate = 30f;

        // 削除されるフレームの最大値から時間オフセットを計算
        float timeOffset = 0f;
        if (matchingRule.shiftToZeroFrame && matchingRule.framesToDelete != null && matchingRule.framesToDelete.Count > 0)
        {
            int maxDeletedFrame = matchingRule.framesToDelete.Max();
            timeOffset = (maxDeletedFrame + 1) / frameRate; // 最大削除フレーム+1フレーム分をオフセット
        }

        // カーブを取得
        EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(clip);

        foreach (var binding in curveBindings)
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.keys.Length == 0)
                continue;

            // 1. 指定フレームを削除
            if (matchingRule.framesToDelete != null && matchingRule.framesToDelete.Count > 0)
            {
                curve = DeleteFrames(curve, matchingRule.framesToDelete, frameRate);
            }

            // 2. 全カーブ共通の時間オフセットを適用
            if (matchingRule.shiftToZeroFrame && timeOffset > 0f)
            {
                curve = ShiftByTime(curve, timeOffset);
            }

            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        ApplyAnimationEvents(clip, matchingRule.animationEvents);

        EditorUtility.SetDirty(clip);
    }

    private void ApplyAnimationEvents(AnimationClip clip, List<AnimationEventRule> eventRules)
    {
        if (eventRules == null || eventRules.Count == 0)
            return;

        float clipLength = clip.length;
        if (clipLength <= 0f)
        {
            Debug.LogWarning($"[FBX Animation Extractor] Cannot place Animation Events on '{clip.name}' (clip length is 0).");
            return;
        }

        List<AnimationEvent> events = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(clip));

        foreach (AnimationEventRule rule in eventRules)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.functionName))
                continue;

            float normalized = Mathf.Clamp01(rule.normalizedTime);
            AnimationEvent animationEvent = new AnimationEvent
            {
                functionName = rule.functionName.Trim(),
                time = clipLength * normalized,
                floatParameter = rule.floatParameter,
                intParameter = rule.intParameter,
                stringParameter = rule.stringParameter ?? string.Empty,
                objectReferenceParameter = rule.objectReferenceParameter,
                messageOptions = SendMessageOptions.DontRequireReceiver,
            };
            events.Add(animationEvent);
        }

        events.Sort((a, b) => a.time.CompareTo(b.time));
        AnimationUtility.SetAnimationEvents(clip, events.ToArray());
    }

    private AnimationPostProcessRule FindMatchingRule(string name)
    {
        if (settings.postProcessRules == null || settings.postProcessRules.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(name))
            return null;

        string normalizedName = name.Trim();

        foreach (var rule in settings.postProcessRules)
        {
            if (string.IsNullOrWhiteSpace(rule.targetName))
                continue;

            if (string.Equals(normalizedName, rule.targetName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Rule Signature: 差分スキップ用の署名生成
    // ═══════════════════════════════════════════════════════════════
    private string BuildRuleSignature(AnimationPostProcessRule rule)
    {
        if (rule == null)
        {
            return "NO_RULE";
        }

        StringBuilder signatureBuilder = new StringBuilder();
        signatureBuilder.Append(rule.targetName?.Trim() ?? string.Empty);
        signatureBuilder.Append("|avatarEnabled:").Append(rule.useOtherAvatarDefinition);
        signatureBuilder.Append("|avatar:").Append(GetAvatarSignature(rule));
        signatureBuilder.Append("|shiftToZeroFrame:").Append(rule.shiftToZeroFrame);
        signatureBuilder.Append("|genericExtract:").Append(rule.genericExtract);
        signatureBuilder.Append("|ignoreScaleKey:").Append(rule.ignoreScaleKey);
        signatureBuilder.Append("|fixScale:").Append(rule.fixScale);

        signatureBuilder.Append("|frames:");
        if (rule.framesToDelete != null)
        {
            for (int i = 0; i < rule.framesToDelete.Count; i++)
            {
                if (i > 0)
                {
                    signatureBuilder.Append(",");
                }
                signatureBuilder.Append(rule.framesToDelete[i]);
            }
        }

        signatureBuilder.Append("|fixScaleObjects:");
        if (rule.fixScaleObjects != null)
        {
            for (int i = 0; i < rule.fixScaleObjects.Count; i++)
            {
                if (i > 0)
                {
                    signatureBuilder.Append(",");
                }
                signatureBuilder.Append(rule.fixScaleObjects[i]?.Trim() ?? string.Empty);
            }
        }

        signatureBuilder.Append("|animationEvents:");
        if (rule.animationEvents != null)
        {
            for (int i = 0; i < rule.animationEvents.Count; i++)
            {
                if (i > 0)
                {
                    signatureBuilder.Append(";");
                }

                AnimationEventRule eventRule = rule.animationEvents[i];
                if (eventRule == null)
                {
                    continue;
                }

                signatureBuilder.Append(eventRule.functionName?.Trim() ?? string.Empty);
                signatureBuilder.Append(">").Append(eventRule.normalizedTime.ToString("R"));
                signatureBuilder.Append(">").Append(eventRule.floatParameter.ToString("R"));
                signatureBuilder.Append(">").Append(eventRule.intParameter);
                signatureBuilder.Append(">").Append(eventRule.stringParameter?.Trim() ?? string.Empty);
                signatureBuilder.Append(">").Append(GetObjectReferenceSignature(eventRule.objectReferenceParameter));
            }
        }

        signatureBuilder.Append("|extractTargets:");
        if (rule.genericExtractTargets != null)
        {
            for (int i = 0; i < rule.genericExtractTargets.Count; i++)
            {
                if (i > 0)
                {
                    signatureBuilder.Append(";");
                }

                GenericExtractTargetRule target = rule.genericExtractTargets[i];
                if (target == null)
                {
                    continue;
                }

                signatureBuilder.Append(target.targetObjectName?.Trim() ?? string.Empty);
                signatureBuilder.Append(">").Append(target.enableRepath);
                signatureBuilder.Append(">").Append(target.repathTo?.Trim() ?? string.Empty);
            }
        }

        return signatureBuilder.ToString();
    }

    private string GetObjectReferenceSignature(UnityEngine.Object objectReference)
    {
        if (objectReference == null)
        {
            return string.Empty;
        }

        string assetPath = AssetDatabase.GetAssetPath(objectReference);
        if (string.IsNullOrEmpty(assetPath))
        {
            return objectReference.name;
        }

        return AssetDatabase.AssetPathToGUID(assetPath);
    }

    private string GetAvatarSignature(AnimationPostProcessRule rule)
    {
        if (rule == null || !rule.useOtherAvatarDefinition || rule.avatarDefinition == null)
        {
            return string.Empty;
        }

        string avatarPath = AssetDatabase.GetAssetPath(rule.avatarDefinition);
        if (string.IsNullOrEmpty(avatarPath))
        {
            return rule.avatarDefinition.name;
        }

        return AssetDatabase.AssetPathToGUID(avatarPath);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Generic Extract: 非ヒューマノイドTransformアニメの抽出/マージ
    // ═══════════════════════════════════════════════════════════════
    private void MergeGenericTransformAnimation(AnimationClip targetClip, string fbxPath, List<GenericExtractTargetRule> extractTargets, bool fixScale, List<string> fixScaleObjects, bool ignoreScaleKey)
    {
        HashSet<string> humanoidBoneNames = CollectHumanoidBoneNames(fbxPath);

        // Pass 1: all Generic objects except fixScaleObjects (scale 1x)
        ProcessGenericExtractPass(targetClip, fbxPath, 1f, extractTargets, fixScaleObjects, false, fixScale, humanoidBoneNames, ignoreScaleKey);

        // Pass 2: fixScaleObjects only (scale 100x), only when Fix Scale is enabled with targets
        if (fixScale && fixScaleObjects != null && fixScaleObjects.Count > 0)
        {
            ProcessGenericExtractPass(targetClip, fbxPath, 100f, extractTargets, fixScaleObjects, true, fixScale, humanoidBoneNames, ignoreScaleKey);
        }

        EditorUtility.SetDirty(targetClip);
    }

    private void ProcessGenericExtractPass(AnimationClip targetClip, string fbxPath, float scale, List<GenericExtractTargetRule> extractTargets, List<string> targetObjects, bool isTargetPass, bool fixScale, HashSet<string> humanoidBoneNames, bool ignoreScaleKey)
    {
        string suffix = isTargetPass ? "_temp_generic_target" : "_temp_generic";
        string dupPath = fbxPath.Replace(".fbx", $"{suffix}.fbx").Replace(".FBX", $"{suffix}.FBX");

        AssetDatabase.CopyAsset(fbxPath, dupPath);

        ModelImporter importer = AssetImporter.GetAtPath(dupPath) as ModelImporter;
        if (importer != null)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.globalScale = scale;
            importer.SaveAndReimport();

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(dupPath);
            AnimationClip genericClip = assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

            if (genericClip != null)
            {
                EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(genericClip);
                foreach (var binding in curveBindings)
                {
                    if (!string.IsNullOrEmpty(binding.path) && !IsHumanoidBonePath(binding.path, humanoidBoneNames))
                    {
                        GenericExtractTargetRule matchedExtractTarget;
                        string relativePathFromTarget;
                        if (!TryMatchGenericExtractTarget(binding.path, extractTargets, out matchedExtractTarget, out relativePathFromTarget))
                        {
                            continue;
                        }

                        bool isTargetObj = false;
                        if (targetObjects != null)
                        {
                            foreach (var obj in targetObjects)
                            {
                                if (!string.IsNullOrEmpty(obj) && binding.path.IndexOf(obj, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    isTargetObj = true;
                                    break;
                                }
                            }
                        }

                        // FixScaleが無効の場合、または指定オブジェクトが空の場合は1回目の抽出(isTargetPass==false)で全て抽出する
                        bool fixScaleTargetActive = fixScale && targetObjects != null && targetObjects.Count > 0;

                        bool shouldExtract = false;
                        if (fixScaleTargetActive)
                        {
                            // 対象パスなら対象のものを追加し、通常パスなら対象以外のものを追加
                            shouldExtract = (isTargetPass == isTargetObj);
                        }
                        else
                        {
                            // FixScale機能を使用しない場合は、通常パス(isTargetPass==false)で全て追加
                            shouldExtract = !isTargetPass;
                        }

                        if (shouldExtract)
                        {
                            if (ignoreScaleKey && IsScaleProperty(binding.propertyName))
                                continue;

                            AnimationCurve curve = AnimationUtility.GetEditorCurve(genericClip, binding);
                            EditorCurveBinding outputBinding = ApplyRepathToBinding(binding, matchedExtractTarget, relativePathFromTarget);
                            AnimationUtility.SetEditorCurve(targetClip, outputBinding, curve);
                        }
                    }
                }
            }
        }

        AssetDatabase.DeleteAsset(dupPath);
    }

    private bool TryMatchGenericExtractTarget(string bindingPath, List<GenericExtractTargetRule> extractTargets, out GenericExtractTargetRule matchedTarget, out string relativePathFromTarget)
    {
        matchedTarget = null;
        string normalizedBindingPath = NormalizeHierarchyPath(bindingPath);
        relativePathFromTarget = normalizedBindingPath;

        if (extractTargets == null || extractTargets.Count == 0)
            return true;

        List<GenericExtractTargetRule> validTargets = extractTargets
            .Where(target => target != null && !string.IsNullOrWhiteSpace(target.targetObjectName))
            .ToList();

        if (validTargets.Count == 0)
            return true;

        foreach (GenericExtractTargetRule target in validTargets)
        {
            string targetNameOrPath = NormalizeHierarchyPath(target.targetObjectName);
            if (string.IsNullOrEmpty(targetNameOrPath))
                continue;

            if (TryGetRelativePathFromTarget(normalizedBindingPath, targetNameOrPath, out string resolvedRelativePath))
            {
                matchedTarget = target;
                relativePathFromTarget = resolvedRelativePath;
                return true;
            }
        }

        return false;
    }

    private bool TryGetRelativePathFromTarget(string bindingPath, string targetNameOrPath, out string relativePathFromTarget)
    {
        relativePathFromTarget = string.Empty;

        if (string.IsNullOrEmpty(bindingPath) || string.IsNullOrEmpty(targetNameOrPath))
            return false;

        bool isHierarchyPathTarget = targetNameOrPath.IndexOf('/') >= 0;
        if (isHierarchyPathTarget)
        {
            if (string.Equals(bindingPath, targetNameOrPath, StringComparison.OrdinalIgnoreCase))
            {
                relativePathFromTarget = string.Empty;
                return true;
            }

            string prefix = $"{targetNameOrPath}/";
            if (bindingPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                relativePathFromTarget = bindingPath.Substring(prefix.Length);
                return true;
            }

            return false;
        }

        string[] pathSegments = bindingPath.Split('/');
        for (int i = 0; i < pathSegments.Length; i++)
        {
            if (!string.Equals(pathSegments[i], targetNameOrPath, StringComparison.OrdinalIgnoreCase))
                continue;

            relativePathFromTarget = i >= pathSegments.Length - 1
                ? string.Empty
                : string.Join("/", pathSegments.Skip(i + 1).ToArray());
            return true;
        }

        return false;
    }

    private EditorCurveBinding ApplyRepathToBinding(EditorCurveBinding binding, GenericExtractTargetRule matchedTarget, string relativePathFromTarget)
    {
        if (matchedTarget == null || !matchedTarget.enableRepath)
            return binding;

        string repathBase = NormalizeHierarchyPath(matchedTarget.repathTo);
        if (string.IsNullOrEmpty(repathBase))
            return binding;

        EditorCurveBinding repathedBinding = binding;
        repathedBinding.path = string.IsNullOrEmpty(relativePathFromTarget)
            ? repathBase
            : $"{repathBase}/{relativePathFromTarget}";
        return repathedBinding;
    }

    private string NormalizeHierarchyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string[] segments = path
            .Split('/')
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return string.Join("/", segments);
    }

    private HashSet<string> CollectHumanoidBoneNames(string fbxPath)
    {
        HashSet<string> humanoidBoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ModelImporter sourceImporter = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (sourceImporter == null)
            return humanoidBoneNames;

        HumanDescription humanDescription = sourceImporter.humanDescription;
        if (humanDescription.human == null)
            return humanoidBoneNames;

        foreach (HumanBone humanBone in humanDescription.human)
        {
            if (!string.IsNullOrEmpty(humanBone.boneName))
            {
                humanoidBoneNames.Add(humanBone.boneName);
            }
        }

        return humanoidBoneNames;
    }

    private bool IsHumanoidBonePath(string path, HashSet<string> humanoidBoneNames)
    {
        string leafName = GetPathLeafName(path);
        if (string.IsNullOrEmpty(leafName))
            return false;

        if (humanoidBoneNames != null && humanoidBoneNames.Count > 0)
        {
            return humanoidBoneNames.Contains(leafName);
        }

        // Fallback: ヒューマノイド情報が取れない場合のみ末端ノード名でゆるく判定
        string lowerLeaf = leafName.ToLowerInvariant();
        string[] humanoidBoneKeywords =
        {
            "spine", "pelvis", "neck", "head", "clavicle", "shoulder", "hand", "leg", "foot",
            "toe", "hip", "thigh", "calf", "root", "finger", "thumb", "chest"
        };

        for (int i = 0; i < humanoidBoneKeywords.Length; i++)
        {
            if (lowerLeaf.Contains(humanoidBoneKeywords[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsScaleProperty(string propertyName)
    {
        return propertyName == "m_LocalScale.x"
            || propertyName == "m_LocalScale.y"
            || propertyName == "m_LocalScale.z";
    }

    private string GetPathLeafName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        int lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash >= path.Length - 1)
            return path;

        return path.Substring(lastSlash + 1);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Curve helpers: フレーム削除 / 時間シフト
    // ═══════════════════════════════════════════════════════════════
    private AnimationCurve DeleteFrames(AnimationCurve curve, List<int> framesToDelete, float frameRate)
    {
        // 削除対象のフレームを時間に変換
        HashSet<float> timesToDelete = new HashSet<float>();
        foreach (int frame in framesToDelete)
        {
            float time = frame / frameRate;
            timesToDelete.Add(time);
        }

        // 削除対象でないキーのみを保持
        List<Keyframe> newKeys = new List<Keyframe>();
        float tolerance = 0.0001f;

        foreach (var key in curve.keys)
        {
            bool shouldDelete = false;
            foreach (float deleteTime in timesToDelete)
            {
                if (Mathf.Abs(key.time - deleteTime) < tolerance)
                {
                    shouldDelete = true;
                    break;
                }
            }

            if (!shouldDelete)
            {
                newKeys.Add(key);
            }
        }

        // 新しいカーブを作成（接線計算は後で行う）
        AnimationCurve newCurve = new AnimationCurve(newKeys.ToArray());
        return newCurve;
    }

    private AnimationCurve ShiftByTime(AnimationCurve curve, float timeOffset)
    {
        if (curve.keys.Length == 0)
            return curve;

        // 全てのキーから指定された時間をオフセット（Keyframeは構造体なので新しい配列を作成）
        Keyframe[] oldKeys = curve.keys;
        Keyframe[] newKeys = new Keyframe[oldKeys.Length];

        for (int i = 0; i < oldKeys.Length; i++)
        {
            Keyframe key = oldKeys[i];
            key.time -= timeOffset;
            newKeys[i] = key;
        }

        return new AnimationCurve(newKeys);
    }
}
