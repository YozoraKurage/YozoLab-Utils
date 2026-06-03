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
    /// <summary>
    /// 抽出後クリップにルールを適用する。
    /// Generic Extract が Separate モードのときは、非Humanoidカーブを格納した別クリップ(in-memory)を返す。
    /// Merge モード/未設定時は null を返す。
    /// </summary>
    private AnimationClip ApplyPostProcessRules(AnimationClip clip, string clipName, string sourceFbxPath)
    {
        AnimationPostProcessRule matchingRule = FindMatchingRule(clipName);

        if (matchingRule == null)
            return null;

        bool needsGenericReimport = matchingRule.genericExtract
            || (matchingRule.eventMarkers != null && matchingRule.eventMarkers.Count > 0);

        // EventMarker の集計結果。functionName ごとに発火時刻のセットを集める。
        // 同じ marker が複数キー時刻を持つ前提なので、複数の Event が打たれる。
        var markerEventSources = new List<MarkerEventSource>();
        AnimationClip separateGenericClip = null;

        if (needsGenericReimport)
        {
            separateGenericClip = ProcessGenericExtractAndEventMarkers(clip, sourceFbxPath, matchingRule, markerEventSources);
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

        ApplyCurveFrameAdjustments(clip, matchingRule, frameRate, timeOffset);
        if (separateGenericClip != null)
        {
            ApplyCurveFrameAdjustments(separateGenericClip, matchingRule, frameRate, timeOffset);
            separateGenericClip.frameRate = frameRate;
        }

        // FBXキー由来のEventをHumanoid clipへ注入(時間shift/delete を反映)
        ApplyMarkerEvents(clip, markerEventSources, matchingRule, frameRate, timeOffset);

        // 手書きAnimationEventsの注入(後方互換・非推奨)
        ApplyAnimationEvents(clip, matchingRule.animationEvents);

        EditorUtility.SetDirty(clip);
        if (separateGenericClip != null)
        {
            EditorUtility.SetDirty(separateGenericClip);
        }

        return separateGenericClip;
    }

    private void ApplyCurveFrameAdjustments(AnimationClip clip, AnimationPostProcessRule rule, float frameRate, float timeOffset)
    {
        EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(clip);

        foreach (var binding in curveBindings)
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.keys.Length == 0)
                continue;

            // 1. 指定フレームを削除
            if (rule.framesToDelete != null && rule.framesToDelete.Count > 0)
            {
                curve = DeleteFrames(curve, rule.framesToDelete, frameRate);
            }

            // 2. 全カーブ共通の時間オフセットを適用
            if (rule.shiftToZeroFrame && timeOffset > 0f)
            {
                curve = ShiftByTime(curve, timeOffset);
            }

            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
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
        signatureBuilder.Append("|genericOutputMode:").Append((int)rule.genericOutputMode);
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

        signatureBuilder.Append("|eventMarkers:");
        if (rule.eventMarkers != null)
        {
            for (int i = 0; i < rule.eventMarkers.Count; i++)
            {
                if (i > 0)
                {
                    signatureBuilder.Append(";");
                }

                EventMarkerRule marker = rule.eventMarkers[i];
                if (marker == null)
                {
                    continue;
                }

                signatureBuilder.Append(marker.targetObjectName?.Trim() ?? string.Empty);
                signatureBuilder.Append(">").Append(marker.functionName?.Trim() ?? string.Empty);
                signatureBuilder.Append(">").Append(marker.floatParameter.ToString("R"));
                signatureBuilder.Append(">").Append(marker.intParameter);
                signatureBuilder.Append(">").Append(marker.stringParameter?.Trim() ?? string.Empty);
                signatureBuilder.Append(">").Append(GetObjectReferenceSignature(marker.objectReferenceParameter));
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
    //  Generic Extract: 非ヒューマノイドTransformアニメの抽出 + EventMarker走査
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FBX由来Eventの集計バッファ。functionName と紐付くイベント発火時刻(threshold超フレーム)を集める。
    /// ProcessGenericReimportPass 内で binding ごとに追加する。
    /// </summary>
    private struct MarkerEventSource
    {
        public EventMarkerRule marker;
        public float time;
    }

    /// <summary>
    /// FBX を Generic として再インポートし、(1) EventMarker 対象のキー時刻を収集
    /// (2) GenericExtract 対象のカーブを Merge 先 / Separate 先 に書き出す。
    /// マーカー側だけ圧縮Offで取得し、prop curve 側は既定圧縮(KeyframeReduction)で抽出するため
    /// 再インポートを分ける(マーカー収集パス / 抽出パス)。
    /// Separate モードのときは生成した別clip(in-memory)を返す。
    /// </summary>
    private AnimationClip ProcessGenericExtractAndEventMarkers(AnimationClip humanoidClip, string fbxPath, AnimationPostProcessRule rule, List<MarkerEventSource> markerEventSources)
    {
        HashSet<string> humanoidBoneNames = CollectHumanoidBoneNames(fbxPath);

        bool separateMode = rule.genericExtract && rule.genericOutputMode == GenericOutputMode.Separate;
        AnimationClip genericOutputClip = separateMode ? new AnimationClip { name = humanoidClip.name + "_generic" } : humanoidClip;

        // Separate モードでは Humanoid clip 側に紛れ込んでいる非Humanoidカーブを除去しておく。
        // Generic 出力先と元clipが同じだと二重に持つことになるため。
        if (separateMode)
        {
            StripNonHumanoidCurves(humanoidClip);
        }

        bool hasMarkers = rule.eventMarkers != null && rule.eventMarkers.Count > 0;

        // (M) マーカー収集パス: prop curve は出力せず、値パルスの波形だけを取得する。
        // 値の変化を threshold(0.5) 判定するので圧縮で疎キーが消えても問題ないが、
        // パルス波形を忠実にサンプリングするため圧縮Off(error=0)で取り込む。
        if (hasMarkers)
        {
            ProcessGenericReimportPass(
                humanoidClip, genericOutputClip, fbxPath,
                scale: 1f,
                rule: rule,
                humanoidBoneNames: humanoidBoneNames,
                markerEventSources: markerEventSources,
                isTargetPass: false,
                compression: ModelImporterAnimationCompression.Off,
                extractCurves: false);
        }

        // (E1) 抽出パス: 既定圧縮(KeyframeReduction)で prop curve を取り出す。マーカー走査はしない。
        if (rule.genericExtract)
        {
            ProcessGenericReimportPass(
                humanoidClip, genericOutputClip, fbxPath,
                scale: 1f,
                rule: rule,
                humanoidBoneNames: humanoidBoneNames,
                markerEventSources: null,
                isTargetPass: false,
                compression: ModelImporterAnimationCompression.KeyframeReduction,
                extractCurves: true);

            // (E2) FixScale パス: scale=100 で fixScaleObjects のみ抽出。
            if (rule.fixScale && rule.fixScaleObjects != null && rule.fixScaleObjects.Count > 0)
            {
                ProcessGenericReimportPass(
                    humanoidClip, genericOutputClip, fbxPath,
                    scale: 100f,
                    rule: rule,
                    humanoidBoneNames: humanoidBoneNames,
                    markerEventSources: null,
                    isTargetPass: true,
                    compression: ModelImporterAnimationCompression.KeyframeReduction,
                    extractCurves: true);
            }
        }

        EditorUtility.SetDirty(humanoidClip);

        if (separateMode && HasAnyCurves(genericOutputClip))
        {
            return genericOutputClip;
        }

        // Separate モードでもカーブが1本も無ければ別clipは作らない。
        return null;
    }

    private void ProcessGenericReimportPass(
        AnimationClip humanoidClip,
        AnimationClip genericOutputClip,
        string fbxPath,
        float scale,
        AnimationPostProcessRule rule,
        HashSet<string> humanoidBoneNames,
        List<MarkerEventSource> markerEventSources,
        bool isTargetPass,
        ModelImporterAnimationCompression compression,
        bool extractCurves)
    {
        string suffix = isTargetPass ? "_temp_generic_target" : "_temp_generic";
        string dupPath = fbxPath.Replace(".fbx", $"{suffix}.fbx").Replace(".FBX", $"{suffix}.FBX");

        AssetDatabase.CopyAsset(fbxPath, dupPath);

        try
        {
            ModelImporter importer = AssetImporter.GetAtPath(dupPath) as ModelImporter;
            if (importer == null) return;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.globalScale = scale;
            importer.animationCompression = compression;

            if (compression == ModelImporterAnimationCompression.Off)
            {
                // マーカーパス: 値パルスの波形を歪ませないよう error=0 で取り込む
                importer.animationPositionError = 0f;
                importer.animationRotationError = 0f;
                importer.animationScaleError    = 0f;
            }
            // else: Unity 既定の error 値で curve fitting を行う(prop curve はサイズ最適化)

            importer.SaveAndReimport();

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(dupPath);
            AnimationClip genericClip = assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (genericClip == null) return;

            bool collectMarkers = markerEventSources != null;
            bool fixScaleTargetActive = rule.genericExtract
                && rule.fixScale
                && rule.fixScaleObjects != null
                && rule.fixScaleObjects.Count > 0;

            EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(genericClip);
            foreach (var binding in curveBindings)
            {
                if (string.IsNullOrEmpty(binding.path) || IsHumanoidBonePath(binding.path, humanoidBoneNames))
                    continue;

                // --- (1) EventMarker 走査 (マーカー収集パスのみ) ------------------------------
                // 値パルス方式: マーカーは local position に「イベント無し=0付近 / イベント有り>0.5」の
                // パルスとして打たれる。値の変化をしきい値判定するので、resample/compression で
                // 疎キーが消えても問題ない。scale(基準値1)/rotation(quaternion w=1)は基準値が0でないため、
                // 基準値0の position チャンネルのみを判定対象にする。
                if (collectMarkers && IsPositionProperty(binding.propertyName))
                {
                    EventMarkerRule matchedMarker = TryMatchEventMarker(binding.path, rule.eventMarkers);
                    if (matchedMarker != null)
                    {
                        AnimationCurve markerCurve = AnimationUtility.GetEditorCurve(genericClip, binding);
                        CollectThresholdMarkerEvents(markerCurve, matchedMarker, genericClip.frameRate, markerEventSources);
                    }
                }

                // --- (2) Generic Extract 出力 (抽出パスのみ) ---------------------------------
                if (!extractCurves)
                    continue;

                // マーカーオブジェクトは prop curve として出力しない (姿勢キーは捨てる)。
                if (TryMatchEventMarker(binding.path, rule.eventMarkers) != null)
                    continue;

                GenericExtractTargetRule matchedExtractTarget;
                string relativePathFromTarget;
                if (!TryMatchGenericExtractTarget(binding.path, rule.genericExtractTargets, out matchedExtractTarget, out relativePathFromTarget))
                    continue;

                bool isTargetObj = false;
                if (rule.fixScaleObjects != null)
                {
                    foreach (var obj in rule.fixScaleObjects)
                    {
                        if (!string.IsNullOrEmpty(obj) && binding.path.IndexOf(obj, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isTargetObj = true;
                            break;
                        }
                    }
                }

                bool shouldExtract;
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

                if (!shouldExtract)
                    continue;

                if (rule.ignoreScaleKey && IsScaleProperty(binding.propertyName))
                    continue;

                AnimationCurve curve = AnimationUtility.GetEditorCurve(genericClip, binding);
                EditorCurveBinding outputBinding = ApplyRepathToBinding(binding, matchedExtractTarget, relativePathFromTarget);
                AnimationUtility.SetEditorCurve(genericOutputClip, outputBinding, curve);
            }
        }
        finally
        {
            AssetDatabase.DeleteAsset(dupPath);
        }
    }

    private EventMarkerRule TryMatchEventMarker(string bindingPath, List<EventMarkerRule> markers)
    {
        if (markers == null || markers.Count == 0)
            return null;

        foreach (EventMarkerRule marker in markers)
        {
            if (marker == null || string.IsNullOrWhiteSpace(marker.targetObjectName) || string.IsNullOrWhiteSpace(marker.functionName))
                continue;

            string targetNameOrPath = NormalizeHierarchyPath(marker.targetObjectName);
            if (string.IsNullOrEmpty(targetNameOrPath))
                continue;

            if (TryGetRelativePathFromTarget(NormalizeHierarchyPath(bindingPath), targetNameOrPath, out _))
            {
                return marker;
            }
        }

        return null;
    }

    /// <summary>
    /// 収集した MarkerEventSource を AnimationEvent として humanoid clip に注入する。
    /// 同じ marker・同じ時刻が複数 binding(position x/y/z)から重複して入るため、
    /// (marker, time) 単位で重複排除してから打つ。
    /// shiftToZeroFrame / framesToDelete の調整も AnimationCurve と同じ規則で反映する。
    /// </summary>
    private void ApplyMarkerEvents(AnimationClip clip, List<MarkerEventSource> sources, AnimationPostProcessRule rule, float frameRate, float timeOffset)
    {
        if (sources == null || sources.Count == 0)
            return;

        float clipLength = clip.length;
        if (clipLength <= 0f)
        {
            Debug.LogWarning($"[FBX Animation Extractor] Cannot place marker Animation Events on '{clip.name}' (clip length is 0).");
            return;
        }

        // framesToDelete をキー時刻の HashSet 化 (許容誤差で吸収)
        HashSet<float> deletedTimes = null;
        if (rule.framesToDelete != null && rule.framesToDelete.Count > 0)
        {
            deletedTimes = new HashSet<float>();
            foreach (int frame in rule.framesToDelete)
            {
                deletedTimes.Add(frame / frameRate);
            }
        }

        // (marker, adjustedTime) で重複排除
        var seen = new HashSet<(EventMarkerRule, long)>();
        var newEvents = new List<AnimationEvent>();
        const float tolerance = 0.0001f;

        foreach (MarkerEventSource source in sources)
        {
            float time = source.time;

            // フレーム削除済みなら捨てる
            if (deletedTimes != null)
            {
                bool dropped = false;
                foreach (float deleted in deletedTimes)
                {
                    if (Mathf.Abs(time - deleted) < tolerance)
                    {
                        dropped = true;
                        break;
                    }
                }
                if (dropped) continue;
            }

            // 時間shift適用
            if (rule.shiftToZeroFrame && timeOffset > 0f)
            {
                time -= timeOffset;
            }

            if (time < 0f || time > clipLength + tolerance)
                continue;

            // (marker, time) で重複排除 (frame粒度で量子化)
            long quantized = (long)Mathf.Round(time * frameRate);
            if (!seen.Add((source.marker, quantized)))
                continue;

            newEvents.Add(new AnimationEvent
            {
                functionName = source.marker.functionName.Trim(),
                time = time,
                floatParameter = source.marker.floatParameter,
                intParameter = source.marker.intParameter,
                stringParameter = source.marker.stringParameter ?? string.Empty,
                objectReferenceParameter = source.marker.objectReferenceParameter,
                messageOptions = SendMessageOptions.DontRequireReceiver,
            });
        }

        if (newEvents.Count == 0)
            return;

        List<AnimationEvent> combined = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(clip));
        combined.AddRange(newEvents);
        combined.Sort((a, b) => a.time.CompareTo(b.time));
        AnimationUtility.SetAnimationEvents(clip, combined.ToArray());
    }

    private static void StripNonHumanoidCurves(AnimationClip clip)
    {
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        foreach (var binding in bindings)
        {
            // Humanoid muscle / root motion カーブは path が空。
            // path が非空 = 個別 Transform 駆動 = Generic 側の責務。
            if (!string.IsNullOrEmpty(binding.path))
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
            }
        }
    }

    private static bool HasAnyCurves(AnimationClip clip)
    {
        return AnimationUtility.GetCurveBindings(clip).Length > 0
            || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0;
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

    private static bool IsPositionProperty(string propertyName)
    {
        return propertyName == "m_LocalPosition.x"
            || propertyName == "m_LocalPosition.y"
            || propertyName == "m_LocalPosition.z";
    }

    /// <summary>
    /// 値パルス方式のマーカー検出。
    /// curve をフレーム単位でサンプリングし、値が threshold(0.5) を超える各フレームにイベントを立てる
    /// (しきい値を上回っている間は連続して打つ)。
    /// 「イベント無し=0付近 / イベント有り>0.5」を local position に打つ運用。
    /// 値の変化として埋め込むため KeyframeReduction でも残り、resample にも左右されない。
    /// </summary>
    private void CollectThresholdMarkerEvents(AnimationCurve curve, EventMarkerRule marker, float frameRate, List<MarkerEventSource> sink)
    {
        const float threshold = 0.5f;

        if (curve == null || curve.keys.Length == 0)
            return;

        if (frameRate <= 0f)
            frameRate = 30f;

        int startFrame = Mathf.FloorToInt(curve.keys[0].time * frameRate);
        int endFrame = Mathf.CeilToInt(curve.keys[curve.keys.Length - 1].time * frameRate);

        for (int frame = startFrame; frame <= endFrame; frame++)
        {
            float time = frame / frameRate;
            if (curve.Evaluate(time) > threshold)
            {
                sink.Add(new MarkerEventSource { marker = marker, time = time });
            }
        }
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
