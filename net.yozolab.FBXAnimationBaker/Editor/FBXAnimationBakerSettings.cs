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
    /// 生成 FBX に何を含めるか。
    /// ModelAndAnimation = メッシュ込みのモデル + アニメーション。
    /// SkeletonOnly      = レンダラー/メッシュを外し、アニメーションするノード階層だけを含める(ファイルが劇的に小さい)。
    /// </summary>
    public enum BakeExportContent
    {
        ModelAndAnimation = 0,
        SkeletonOnly = 1,
    }

    /// <summary>
    /// 生成 FBX のノードに残す「素のポーズ」(アニメーションを評価しない状態の見た目)。
    ///
    /// FBX はノードごとに静的なローカル変換を持ち、テイク(アニメーション)のカーブが
    /// 再生時にそれを上書きする。どちらを基準ポーズとして残すかの選択。
    ///
    /// SourceFbxPose = 元 FBX の姿勢(多くのモデルでは T ポーズ)をそのまま残す。
    ///                 姿勢は全てカーブ側で表現されるため、定数カーブの間引きは行わない。
    /// FirstFrame    = アニメーション 0 フレーム目の姿勢を焼き込む。定数カーブを間引ける。
    /// </summary>
    public enum BakeRestPose
    {
        SourceFbxPose = 0,
        FirstFrame = 1,
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

        [Tooltip("Strip blend shape data from the exported meshes. Blend shapes usually dominate the FBX size. Ignored when Bake BlendShapes is enabled")]
        public bool excludeBlendShapes = false;

        [Tooltip("Which pose the FBX nodes keep when the animation is not evaluated. Source FBX Pose keeps the original (usually T-pose) and puts everything into the take")]
        public BakeRestPose restPose = BakeRestPose.SourceFbxPose;

        [Tooltip("Drop curves whose value never changes over the whole clip (keeps the FBX small). Ignored when Rest Pose is Source FBX Pose")]
        public bool removeConstantCurves = true;

        [Tooltip("Remove keys that sit on a straight line between their neighbours. Greatly reduces the FBX size of per-frame baked curves")]
        public bool keyframeReduction = true;

        [Tooltip("Allowed error for keyframe reduction. Larger = smaller file, less accurate")]
        public float reductionTolerance = 0.0001f;

        [Tooltip("What to include in the generated FBX. Skeleton Only strips meshes/renderers and keeps only the animated node hierarchy")]
        public BakeExportContent exportContent = BakeExportContent.ModelAndAnimation;

        [Tooltip("Also save the baked Transform clip as a .anim asset next to the generated FBX")]
        public bool saveBakedClipAsset = true;

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
