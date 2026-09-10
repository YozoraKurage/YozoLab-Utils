using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using YozoLab.FBXAnimationBaker.Bvh;

namespace YozoLab.FBXAnimationBaker
{
    /// <summary>
    /// ベイクの「ポーズをどこから持ってくるか」を切り出した部分。
    ///
    /// ベイク後の処理（カーブ生成・基準ポーズへの復帰・FBX 書き出し・インポート設定・
    /// 差分キャッシュ）は供給元が何であっても同じなので、違うのは「ある時刻の姿勢を
    /// インスタンスへ当てる」ところだけになる。そこだけを差し替えられるようにしてある。
    /// </summary>
    public partial class FBXAnimationBakerWindow
    {
        /// <summary>
        /// Execute で処理する 1 単位。エントリと、そのモーション元。
        ///
        /// モーション元は AnimationClip か BVH のどちらか。名前・依存ハッシュ・
        /// 署名の取り方だけが違い、それ以外の経路は共通なので、違いをここへ閉じ込める。
        /// </summary>
        private readonly struct BakeJob
        {
            public readonly AnimationBakeEntry Entry;

            /// <summary>クリップ由来なら非 null。</summary>
            public readonly AnimationClip Clip;

            /// <summary>BVH 由来なら "Assets/....bvh"。</summary>
            public readonly string BvhAssetPath;

            /// <summary>同じエントリから複数出るか。出力名の衝突避けに使う。</summary>
            public readonly bool MultiOutput;

            private BakeJob(AnimationBakeEntry entry, AnimationClip clip, string bvhAssetPath, bool multiOutput)
            {
                Entry = entry;
                Clip = clip;
                BvhAssetPath = bvhAssetPath;
                MultiOutput = multiOutput;
            }

            public static BakeJob FromClip(AnimationBakeEntry entry, AnimationClip clip, bool multiOutput)
                => new BakeJob(entry, clip, null, multiOutput);

            public static BakeJob FromBvh(AnimationBakeEntry entry, string bvhAssetPath)
                => new BakeJob(entry, null, bvhAssetPath, false);

            public bool IsBvh => Clip == null;

            public string SourceName => IsBvh
                ? System.IO.Path.GetFileNameWithoutExtension(BvhAssetPath)
                : Clip.name;

            public string SourceAssetPath => IsBvh
                ? BvhAssetPath
                : UnityEditor.AssetDatabase.GetAssetPath(Clip);

            /// <summary>供給元を作る。BVH はここで初めてファイルを読む。</summary>
            public BakePoseSource CreatePoseSource()
            {
                if (!IsBvh) return new ClipPoseSource(Clip);

                string absolute = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    System.IO.Directory.GetParent(Application.dataPath).FullName, BvhAssetPath));

                BvhFile file = BvhFile.Load(absolute);
                return new BvhPoseSource(file, SourceName, Entry.bvhScale, Entry.bvhBoneOverrides);
            }
        }

        /// <summary>
        /// ある時刻の姿勢を、ベイク対象のインスタンスへ当てるもの。
        /// </summary>
        private abstract class BakePoseSource : IDisposable
        {
            /// <summary>ログや出力ファイル名に使う名前。</summary>
            public abstract string Name { get; }

            /// <summary>供給元が持っているフレームレート。エントリ側が 0 のときこれを使う。</summary>
            public abstract float FrameRate { get; }

            /// <summary>秒。0 なら 1 フレーム分の長さが使われる。</summary>
            public abstract float Duration { get; }

            /// <summary>相手に Humanoid Avatar が要るか。</summary>
            public abstract bool RequiresHumanoidAvatar { get; }

            /// <summary>生成クリップへ引き継ぐループ設定。</summary>
            public virtual bool LoopTime => false;

            /// <summary>
            /// サンプリングを AnimationMode で囲む必要があるか。
            ///
            /// AnimationMode は「クリップを当てて覗いたあと元へ戻す」ための仕組みで、
            /// SampleAnimationClip を使う経路にだけ要る。BVH は Transform を直接
            /// 書くので囲む相手がおらず、囲むと Unity が追跡していない書き込みだと
            /// 文句を言う。既定は true、直接書く供給元だけ false にする。
            /// </summary>
            public virtual bool UsesAnimationMode => true;

            /// <summary>サンプリングを始める前に一度だけ呼ばれる。失敗したら例外を投げる。</summary>
            public virtual void Begin(GameObject instance, AnimationBakeEntry entry) { }

            /// <summary>time 秒の姿勢を instance へ当てる。</summary>
            public abstract void SamplePose(GameObject instance, float time);

            public virtual void Dispose() { }
        }

        /// <summary>
        /// 既存の経路。AnimationClip を AnimationMode でサンプリングする。
        /// Humanoid クリップなら、この時点で Unity がリターゲットを済ませてくれる。
        /// </summary>
        private sealed class ClipPoseSource : BakePoseSource
        {
            private readonly AnimationClip clip;

            public ClipPoseSource(AnimationClip clip)
            {
                this.clip = clip;
            }

            public AnimationClip Clip => clip;

            public override string Name => clip.name;
            public override float FrameRate => clip.frameRate;
            public override float Duration => clip.length;
            public override bool RequiresHumanoidAvatar => clip.isHumanMotion;
            public override bool LoopTime => AnimationUtility.GetAnimationClipSettings(clip).loopTime;

            public override void SamplePose(GameObject instance, float time)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(instance, clip, time);
                AnimationMode.EndSampling();
            }
        }

        /// <summary>
        /// BVH を供給元にする経路。
        ///
        /// BVH のジョイント名や骨の長さは配布元ごとにばらばらで、対象モデルの
        /// ボーン構成とは一致しない。名前で対応付けて回転を移すやり方だと、
        /// 骨の長さの違い・軸の取り方・回転オーダーを全部自分で吸収することになる。
        ///
        /// 代わりに Humanoid を挟む。BVH の骨格にも Humanoid Avatar を組んでしまえば、
        /// あとは筋肉空間のポーズを吸い出して相手へ流すだけで、その差は Unity の
        /// Humanoid 正規化が吸収してくれる。つまり Unity のリターゲットに乗せている。
        /// </summary>
        private sealed class BvhPoseSource : BakePoseSource
        {
            private readonly BvhFile file;
            private readonly string sourceName;
            private readonly float scale;
            private readonly List<BvhBoneOverride> boneOverrides;

            private BvhSkeleton skeleton;
            private Avatar sourceAvatar;
            private HumanPoseHandler sourceHandler;
            private HumanPoseHandler targetHandler;
            private HumanPose pose;
            private bool applyRootMotion = true;

            public BvhPoseSource(BvhFile file, string sourceName, float scale, List<BvhBoneOverride> boneOverrides)
            {
                this.file = file;
                this.sourceName = sourceName;
                this.scale = scale > 0f ? scale : 0.01f;
                this.boneOverrides = boneOverrides;
            }

            public override string Name => sourceName;
            public override float FrameRate => file.FrameRate;
            public override float Duration => Mathf.Max(0, file.Frames.Count - 1) * file.FrameTime;

            // Humanoid を挟む以上、相手にも Humanoid Avatar が要る。
            public override bool RequiresHumanoidAvatar => true;

            // HumanPoseHandler は Transform を直接書く。AnimationMode の管理外。
            public override bool UsesAnimationMode => false;

            public override void Begin(GameObject instance, AnimationBakeEntry entry)
            {
                if (file.Frames.Count == 0)
                {
                    throw new BvhParseException($"\"{sourceName}\" に MOTION のフレームがありません。");
                }

                applyRootMotion = entry.bakeRootMotion;

                skeleton = BvhSkeleton.Build(file, scale, sourceName);
                skeleton.Root.hideFlags = HideFlags.HideAndDontSave;

                Dictionary<string, string> boneMap = BvhHumanoid.BuildBoneMap(file, boneOverrides);
                Debug.Log($"{LogPrefix} {BvhHumanoid.DescribeBoneMap(file, boneMap)}");

                sourceAvatar = BvhHumanoid.BuildAvatar(skeleton, boneMap, out string avatarError);
                if (sourceAvatar == null)
                {
                    throw new BvhParseException($"\"{sourceName}\": {avatarError}");
                }

                Animator animator = instance.GetComponent<Animator>();
                Avatar targetAvatar = animator != null ? animator.avatar : null;
                if (targetAvatar == null || !targetAvatar.isHuman)
                {
                    throw new BvhParseException(
                        "リターゲット先のモデルに Humanoid Avatar がありません。"
                        + "Source FBX の Rig を Humanoid にするか、Use Other Avatar Definition で指定してください。");
                }

                sourceHandler = new HumanPoseHandler(sourceAvatar, skeleton.Root.transform);
                targetHandler = new HumanPoseHandler(targetAvatar, instance.transform);
            }

            public override void SamplePose(GameObject instance, float time)
            {
                // BVH はフレーム列そのもの。補間はせず、いちばん近いフレームを当てる。
                // サンプリング fps を BVH の fps に合わせておけば 1:1 で対応する。
                int frame = Mathf.Clamp(Mathf.RoundToInt(time / file.FrameTime), 0, file.Frames.Count - 1);

                skeleton.ApplyFrame(frame);
                sourceHandler.GetHumanPose(ref pose);

                if (!applyRootMotion)
                {
                    // 足踏みだけ残して水平移動を捨てる。高さは残す(しゃがみ等が潰れるため)。
                    pose.bodyPosition = new Vector3(0f, pose.bodyPosition.y, 0f);
                }

                targetHandler.SetHumanPose(ref pose);
            }

            public override void Dispose()
            {
                sourceHandler?.Dispose();
                targetHandler?.Dispose();

                if (sourceAvatar != null) UnityEngine.Object.DestroyImmediate(sourceAvatar);
                if (skeleton?.Root != null) UnityEngine.Object.DestroyImmediate(skeleton.Root);
            }
        }
    }
}
