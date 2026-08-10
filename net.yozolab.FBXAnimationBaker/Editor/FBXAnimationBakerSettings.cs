using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// FBX Animation Baker の設定。
    ///
    /// FBX Animation Extractor と同様、VPM/UPM 更新でパッケージ配下が丸ごと入れ替わっても
    /// ユーザー設定が消えないよう、設定はパッケージ外の ProjectSettings/ に保存する
    /// ScriptableSingleton として保持する。
    /// </summary>
    [FilePath("ProjectSettings/FBXAnimationBakerSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class FBXAnimationBakerSettings : ScriptableSingleton<FBXAnimationBakerSettings>
    {
        [Tooltip("Default output folder for the generated FBX files")]
        public DefaultAsset outputDirectory;

        [Tooltip("List of bake entries (FBX + humanoid animation clips)")]
        public List<AnimationBakeEntry> bakeEntries = new List<AnimationBakeEntry>();

        [HideInInspector]
        public List<BakeCacheEntry> bakeCacheEntries = new List<BakeCacheEntry>();

        /// <summary>ProjectSettings/ 配下のファイルへ即時保存する。</summary>
        public void SaveSettings()
        {
            Save(true);
        }
    }

    /// <summary>
    /// 生成した FBX の再インポート時に設定する Animation Type。
    /// </summary>
    public enum BakedFbxAnimationType
    {
        None = 0,
        Legacy = 1,
        Generic = 2,
        Humanoid = 3,
    }

    /// <summary>
    /// 1 エントリ = 「1 つの FBX に対して、指定した Humanoid AnimationClip 群を
    /// Transform ベイク済みで同梱した FBX を出力する」単位。
    /// </summary>
    [Serializable]
    public class AnimationBakeEntry
    {
        [Tooltip("Entry name shown in the list (leave empty to use the FBX name)")]
        public string displayName;

        [Tooltip("When OFF, this entry is skipped by Execute")]
        public bool enabled = true;

        [Tooltip("Source FBX (model) the animation is baked onto")]
        public GameObject sourceFbx;

        [Tooltip("Humanoid animation clips to bake. One FBX is generated per clip")]
        public List<AnimationClip> clips = new List<AnimationClip>();

        [Tooltip("Per-entry output folder. When set, FBX files are written here instead of the global Output Directory")]
        public DefaultAsset outputDirectoryOverride;

        [Tooltip("File name of the generated FBX (without extension). Leave empty to use the clip name. With multiple clips the clip name is appended")]
        public string outputFileName;

        [Tooltip("Use an Avatar other than the one built from the source FBX when sampling")]
        public bool useOtherAvatarDefinition = false;

        [Tooltip("Avatar used when Use Other Avatar Definition is enabled")]
        public Avatar avatarDefinition;

        [Tooltip("Sampling frame rate. 0 = use the source clip frame rate")]
        public float frameRate = 0f;

        [Tooltip("Apply root motion while sampling, so the humanoid root motion is baked into the root Transform")]
        public bool bakeRootMotion = true;

        [Tooltip("Bake Transform Scale (m_LocalScale) curves as well")]
        public bool bakeScale = false;

        [Tooltip("Bake SkinnedMeshRenderer blend shape weights driven by the clip")]
        public bool bakeBlendShapes = false;

        [Tooltip("Drop curves whose value never changes over the whole clip (keeps the FBX small)")]
        public bool removeConstantCurves = true;

        [Tooltip("Also save the baked Transform clip as a .anim asset next to the generated FBX")]
        public bool saveBakedClipAsset = false;

        [Tooltip("Animation Type applied to the generated FBX when it is imported back into the project")]
        public BakedFbxAnimationType importAnimationType = BakedFbxAnimationType.Generic;

        [Tooltip("Export the FBX in ASCII instead of binary")]
        public bool exportAscii = false;
    }

    /// <summary>
    /// 差分スキップ用キャッシュ。source FBX / clip の依存ハッシュとエントリ設定の署名を保持する。
    /// </summary>
    [Serializable]
    public class BakeCacheEntry
    {
        public string outputFbxAssetPath;
        public string sourceDependencyHash;
        public string clipDependencyHash;
        public string entrySignature;
    }
}
