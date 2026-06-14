using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.MatVariantizer
{
    /// <summary>マテリアル行の処理状態。</summary>
    internal enum MaterialRowStatus
    {
        /// <summary>新規に Variant を生成する。</summary>
        New,
        /// <summary>既存の生成済み Variant を再利用する。</summary>
        ReuseExisting,
    }

    /// <summary>テクスチャ行の処理状態。</summary>
    internal enum TextureRowStatus
    {
        New,
        ReuseExisting,
        /// <summary>既に自フォルダのコピーを参照済み（対象外）。</summary>
        AlreadyLocalized,
        /// <summary>FBX 等への埋め込み（独立パス無し）でコピー不可。</summary>
        Embedded,
    }

    /// <summary>レンダラーのマテリアルスロット 1 箇所。</summary>
    internal sealed class MaterialSlotRef
    {
        public Renderer renderer;
        public int slotIndex;
        /// <summary>このレンダラーが Prefab インスタンスの一部か（override 追跡可否）。</summary>
        public bool inPrefabInstance;
    }

    /// <summary>あるテクスチャ参照箇所（マテリアル × プロパティ）。</summary>
    internal sealed class TextureRef
    {
        public MaterialPlan material;
        public string property;
    }

    /// <summary>マテリアルが参照するテクスチャ（表示用の逆引き）。</summary>
    internal sealed class TextureProp
    {
        public string property;
        public TexturePlan texture;
    }

    /// <summary>
    /// アバター内の「原本テクスチャ 1 つ」＝集約する 1 コピー。
    /// 同一原本を指す参照は全てこの 1 コピーへ向け直され、編集が同時反映される。
    /// </summary>
    internal sealed class TexturePlan
    {
        public AvatarGroup avatar;
        public Texture originalTexture;
        public string copyPath;
        public TextureRowStatus status;
        public bool selected;
        /// <summary>Apply 後に解決されたローカルコピー。</summary>
        public Texture createdCopy;
        public readonly List<TextureRef> references = new List<TextureRef>();

        /// <summary>実際にローカライズ操作の対象になり得るか。</summary>
        public bool Actionable =>
            status == TextureRowStatus.New || status == TextureRowStatus.ReuseExisting;
    }

    /// <summary>
    /// アバター内の「真の原本マテリアル 1 つ」＝生成する Variant 1 つ。
    /// 参照が既に自フォルダの Variant の場合、original はその parent（真の原本）を指す。
    /// </summary>
    internal sealed class MaterialPlan
    {
        public AvatarGroup avatar;
        public Material original;
        /// <summary>既に生成済みの Variant があれば。</summary>
        public Material existingVariant;
        public string variantPath;
        public MaterialRowStatus status;
        /// <summary>全スロットが既にこの Variant を指している（再 Apply は no-op）。</summary>
        public bool alreadyBound;
        public bool selected;
        /// <summary>UI: テクスチャ子行の開閉。</summary>
        public bool expanded;
        /// <summary>Apply 後に生成/再利用された Variant。</summary>
        public Material createdVariant;
        public readonly List<MaterialSlotRef> slots = new List<MaterialSlotRef>();
        public readonly List<TextureProp> textureProps = new List<TextureProp>();

        /// <summary>いずれかのスロットが Prefab インスタンス外か（override 追跡不可）。</summary>
        public bool AnySlotOutsidePrefab
        {
            get
            {
                foreach (MaterialSlotRef s in slots)
                    if (!s.inPrefabInstance) return true;
                return false;
            }
        }
    }

    /// <summary>1 アバター（VRCAvatarDescriptor 保有ルート、または選択 GameObject）。</summary>
    internal sealed class AvatarGroup
    {
        public GameObject root;
        public string name;
        /// <summary>このアバターの生成物フォルダ（&lt;base&gt;/&lt;AvatarName&gt;）。</summary>
        public string folder;
        public bool expanded = true;
        public readonly List<MaterialPlan> materials = new List<MaterialPlan>();
        public readonly List<TexturePlan> textures = new List<TexturePlan>();
    }

    /// <summary>スキャン結果から構築される全体の計画。</summary>
    internal sealed class LocalizationPlan
    {
        public string baseFolder;
        public readonly List<AvatarGroup> avatars = new List<AvatarGroup>();
        public bool IsEmpty => avatars.Count == 0;
    }
}
