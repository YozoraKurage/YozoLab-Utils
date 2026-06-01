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

    // Separate モードで生成された Generic 専用クリップのパス。Merge モードでは空文字。
    public string generatedGenericClipAssetPath;
}

/// <summary>
/// Generic Extract の出力モード。
/// Merge   = 既存の挙動。非Humanoidカーブを Humanoid clip にマージする。
/// Separate = Humanoid clip と <fbxName>_generic.anim に分離出力する。
/// </summary>
public enum GenericOutputMode
{
    Merge = 0,
    Separate = 1,
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

    [Tooltip("Output mode for Generic Extract. Merge keeps the existing behavior; Separate writes non-humanoid curves to <fbxName>_generic.anim")]
    public GenericOutputMode genericOutputMode = GenericOutputMode.Merge;

    [Tooltip("Event markers driven by FBX keyframes. Keys on the specified object become Animation Events at the same time on the humanoid clip")]
    public List<EventMarkerRule> eventMarkers = new List<EventMarkerRule>();

    [Tooltip("[DEPRECATED] Manually authored Animation Events placed at a normalized time. Prefer eventMarkers driven by FBX keyframes")]
    public List<AnimationEventRule> animationEvents = new List<AnimationEventRule>();
}

/// <summary>
/// Event marker rule that converts FBX keyframes on a specific object
/// into Animation Events on the generated humanoid clip.
/// The target object's curve values are ignored - only key times are used.
/// </summary>
[Serializable]
public class EventMarkerRule
{
    [Tooltip("Target object name or hierarchy path (same matching rule as GenericExtractTargetRule)")]
    public string targetObjectName;

    [Tooltip("Function name invoked by the Animation Event")]
    public string functionName;

    [Tooltip("Float parameter passed to the function")]
    public float floatParameter;

    [Tooltip("Int parameter passed to the function")]
    public int intParameter;

    [Tooltip("String parameter passed to the function")]
    public string stringParameter;

    [Tooltip("Object reference parameter passed to the function")]
    public UnityEngine.Object objectReferenceParameter;
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
