using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using YozoLab.FBXAnimationBaker.Bvh;

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
        // ── 旧: 全エントリ共通の Output Directory ──────────────────────
        // 出力先はフォルダごとの設定へ移した。これは既存の設定ファイルを
        // 読み込んで移行するためだけに残してある。移行後は null。
        [HideInInspector]
        public DefaultAsset outputDirectory;

        /// <summary>共通の Output Directory からフォルダ単位の設定への移行を済ませたか。</summary>
        [HideInInspector]
        public bool perFolderDirectoriesMigrated;

        /// <summary>エントリごとの clips / bvhFile から motions への統合を済ませたか。</summary>
        [HideInInspector]
        public bool motionsMigrated;

        [Tooltip("List of bake entries (FBX + humanoid animation clips)")]
        public List<AnimationBakeEntry> bakeEntries = new List<AnimationBakeEntry>();

        [Tooltip("Folders for organizing the entry list (per-folder output directory / bake flag / foldout)")]
        public List<BakeFolderState> bakeFolders = new List<BakeFolderState>();

        [HideInInspector]
        public List<BakeCacheEntry> bakeCacheEntries = new List<BakeCacheEntry>();

        /// <summary>ProjectSettings/ 配下のファイルへ即時保存する。</summary>
        public void SaveSettings()
        {
            Save(true);
        }
    }

    /// <summary>
    /// Entry List 上のフォルダ。「New Folder」ボタンで明示的に作成・削除される。
    ///
    /// 出力先はここが持つ。以前は全エントリ共通の 1 つしか無く、行き先ごとに
    /// エントリ側の Output Override を全部埋めて回る必要があった。
    /// 空のフォルダも保持される。名前の一致は大文字小文字を無視する。
    /// </summary>
    [Serializable]
    public class BakeFolderState
    {
        public string name;

        [Tooltip("Output folder for entries in this folder. Individual entries can override it")]
        public DefaultAsset outputDirectory;

        [Tooltip("When OFF, entries in this folder are skipped by Execute")]
        public bool bakeEnabled = true;

        public bool expanded = true;
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

        [Tooltip("Name of the folder this entry belongs to (empty = no folder, case-insensitive)")]
        public string folder;

        [Tooltip("Source FBX (model) the animation is baked onto")]
        public GameObject sourceFbx;

        /// <summary>
        /// 焼くモーション。1 つにつき FBX を 1 つ出力する。
        ///
        /// AnimationClip（FBX 内蔵でも .anim でも）と .bvh を同じ一覧に混ぜて置ける。
        /// 以前はクリップ用と BVH 用で欄が分かれていたが、「このエントリは何を
        /// 変換するのか」がひと目で分からず、どちらが使われるのかも読めなかった。
        /// </summary>
        [Tooltip("Motions to bake. Animation clips and .bvh files can be mixed. One FBX is generated per motion")]
        public List<UnityEngine.Object> motions = new List<UnityEngine.Object>();

        // ── 旧: クリップ用と BVH 用に分かれていた欄 ─────────────────────
        // motions へ統合した。既存の設定を読み込んで移行するためだけに残してある。
        // 移行後は空になり、以後は参照されない。

        [HideInInspector]
        public List<AnimationClip> clips = new List<AnimationClip>();

        [HideInInspector]
        public DefaultAsset bvhFile;


        [Tooltip("Which axis the BVH treats as up. Auto reads it from the skeleton's offsets. The spec says Y, but Z is common in practice")]
        public BvhUpAxis bvhUpAxis = BvhUpAxis.Auto;

        [Tooltip("Unit conversion for the BVH skeleton. Retargeting goes through humanoid normalisation, so this only matters when the values are extreme enough to stop an Avatar being built")]
        public float bvhScale = 1f;

        [Tooltip("Fix up how BVH joints map onto Unity humanoid bones. Only needed when the automatic guess misses one")]
        public List<BvhBoneOverride> bvhBoneOverrides = new List<BvhBoneOverride>();

        [Tooltip("Per-entry output folder. When set, FBX files are written here instead of the folder's Output Directory")]
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

        [Tooltip("Drop curves whose value never changes over the whole clip (keeps the FBX small)")]
        public bool removeConstantCurves = true;

        [Tooltip("Remove keys that sit on a straight line between their neighbours. Greatly reduces the FBX size of per-frame baked curves")]
        public bool keyframeReduction = true;

        [Tooltip("Allowed error for keyframe reduction. Larger = smaller file, less accurate")]
        public float reductionTolerance = 0.0001f;

        [Tooltip("What to include in the generated FBX. Skeleton Only strips meshes/renderers and keeps only the animated node hierarchy")]
        public BakeExportContent exportContent = BakeExportContent.ModelAndAnimation;

        [Tooltip("Also save the baked Transform clip as a .anim asset next to the generated FBX")]
        public bool saveBakedClipAsset = true;

        [Tooltip("Skip import work the baked FBX does not need (materials, cameras, lights, blend shapes, tangents). Never touches anything that changes the imported result")]
        public bool fastImport = true;

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
