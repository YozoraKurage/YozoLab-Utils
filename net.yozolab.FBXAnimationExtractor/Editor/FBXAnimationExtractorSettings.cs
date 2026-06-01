using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

/// <summary>
/// FBX Animation Extractor の設定。
///
/// VPM/UPM でパッケージを更新すると、パッケージ配下のファイルは丸ごと入れ替えられる。
/// 以前はこの設定をパッケージ内の .asset に保存していたため、更新のたびにユーザーの設定が
/// 空アセットで上書きされて消えていた。これを防ぐため、設定はパッケージ外の
/// ProjectSettings/ に保存する ScriptableSingleton として保持する。
/// </summary>
[FilePath("ProjectSettings/FBXAnimationExtractorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
public class FBXAnimationExtractorSettings : ScriptableSingleton<FBXAnimationExtractorSettings>
{
    [Tooltip("Folder containing the FBX files to process")]
    public DefaultAsset targetDirectory;

    [Tooltip("Output folder for extracted animation clips")]
    public DefaultAsset outputDirectory;

    [Tooltip("List of animation post-process rules")]
    public List<AnimationPostProcessRule> postProcessRules = new List<AnimationPostProcessRule>();

    [HideInInspector]
    public List<FbxProcessCacheEntry> processCacheEntries = new List<FbxProcessCacheEntry>();

    /// <summary>ProjectSettings/ 配下のファイルへ即時保存する。</summary>
    public void SaveSettings()
    {
        Save(true);
    }
}

[Serializable]
public class FbxProcessCacheEntry
{
    public string fbxAssetPath;
    public string sourceDependencyHash;
    public string ruleSignature;
    public string generatedClipAssetPath;
}

/// <summary>
/// Animation post-process rule applied to matching FBX files.
/// </summary>
[Serializable]
public class AnimationPostProcessRule
{
    [Tooltip("Target animation name (case-insensitive exact match with FBX file name)")]
    public string targetName;

    [Tooltip("Enable Use Other Avatar Definition")]
    public bool useOtherAvatarDefinition = false;

    [Tooltip("Avatar used when Use Other Avatar Definition is enabled")]
    public Avatar avatarDefinition;

    [Tooltip("List of frames to delete (zero-based index)")]
    public List<int> framesToDelete = new List<int>();

    [Tooltip("If no key exists at frame 0, shift the first key to frame 0 and offset all subsequent keys accordingly")]
    public bool shiftToZeroFrame = true;

    [Tooltip("Extract and merge Transform animations of non-humanoid objects (weapons, props, etc.) using Generic animation")]
    public bool genericExtract = false;

    [Tooltip("Target object names for Generic extraction. Only objects under the specified names are extracted (leave empty to extract all)")]
    public List<GenericExtractTargetRule> genericExtractTargets = new List<GenericExtractTargetRule>();

    [Tooltip("Exclude Transform Scale (m_LocalScale) curves when extracting Generic animations")]
    public bool ignoreScaleKey = false;

    [Tooltip("During Generic re-import, set the Scale Factor of specified objects to 100 (Fix Scale)")]
    public bool fixScale = false;

    [Tooltip("Object names to import at Scale Factor 100 during Fix Scale (partial match)")]
    public List<string> fixScaleObjects = new List<string>();

    [Tooltip("Animation Events to place on the generated clip (time is normalized 0-1 relative to clip length)")]
    public List<AnimationEventRule> animationEvents = new List<AnimationEventRule>();
}

/// <summary>
/// Per-target settings for Generic extraction.
/// </summary>
[Serializable]
public class GenericExtractTargetRule
{
    [Tooltip("Target object name or hierarchy path (e.g. Armature or Armature/Hips/..., case-insensitive)")]
    public string targetObjectName;

    [Tooltip("When enabled, remaps paths under targetObjectName to repathTo")]
    public bool enableRepath = false;

    [Tooltip("Destination path for remapping (e.g. Root/Armature)")]
    public string repathTo;
}

/// <summary>
/// Animation Event placed on the generated clip at a normalized time position.
/// </summary>
[Serializable]
public class AnimationEventRule
{
    [Tooltip("Function name invoked by the Animation Event")]
    public string functionName;

    [Tooltip("Position within the clip (0 = start, 1 = end). Default 0.9 = 90%")]
    [Range(0f, 1f)]
    public float normalizedTime = 0.9f;

    [Tooltip("Float parameter passed to the function")]
    public float floatParameter;

    [Tooltip("Int parameter passed to the function")]
    public int intParameter;

    [Tooltip("String parameter passed to the function")]
    public string stringParameter;

    [Tooltip("Object reference parameter passed to the function")]
    public UnityEngine.Object objectReferenceParameter;
}
