using System;
using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// 1つの静的メッシュとして出力されるレンダラーのグループ。
    /// グループごとに別のMesh/プレハブが出力される（マテリアルとテクスチャは全グループ共有）。
    /// </summary>
    [Serializable]
    public class BakeRendererGroup
    {
        [Tooltip("グループ名。成果物アセット名のサフィックスになります（空なら連番）。")]
        public string name = "";

        [Tooltip("このグループとして1つの静的メッシュにまとめるRenderer。" +
                 "SkinnedMeshRenderer・MeshRendererのどちらも指定できます。")]
        public List<Renderer> renderers = new List<Renderer>();
    }

    /// <summary>
    /// テクスチャごとの解像度の上書き設定（非破壊）。
    /// 元テクスチャには手を加えず、アトラス上で割り当てるテクセル密度（=実効解像度）だけを変更する。
    /// 同一テクスチャを複数マテリアルが参照していても、設定は1テクスチャにつき1つで済む。
    /// </summary>
    [Serializable]
    public class TextureResolutionSetting
    {
        public Texture texture;

        [Tooltip("アトラス上でのテクスチャの目標解像度（長辺px）。0なら原寸（上書きなし）。")]
        public int targetResolution = 0;
    }

    /// <summary>
    /// マテリアルの扱い方の抽象モード（Editorウィンドウ上の抽象化）。
    /// 既知のシェーダーに対しては、アトラス対象テクスチャプロパティを自動で決定する。
    /// </summary>
    public enum MaterialMode
    {
        /// <summary>Texture Propertiesに手動指定したプロパティを使う</summary>
        Custom,
        /// <summary>Autodesk Interactiveシェーダーのテクスチャを自動収集する</summary>
        AutodeskInteractive,
        /// <summary>Unity Standardシェーダーのテクスチャを自動収集する（標準）</summary>
        UnityStandard,
        /// <summary>Filamented（Silent/Filamented）シェーダーのテクスチャを自動収集する</summary>
        Filamented,
        /// <summary>Mochie（Mochie/Standard）シェーダーのテクスチャを自動収集する</summary>
        Mochie,
    }

    /// <summary>アトラス化の方式</summary>
    public enum AtlasPackingMode
    {
        /// <summary>UVアイランド単位で詰め直す（充填率が高い。TexTransToolのNFDH+FCアルゴリズムを採用）</summary>
        UVIslands,
        /// <summary>マテリアルごとの矩形単位で詰める（単純・安全）</summary>
        MaterialRects,
    }

    /// <summary>ライトマップ用UV2の扱い</summary>
    public enum LightmapUVMode
    {
        /// <summary>UV2を出力しない</summary>
        None,
        /// <summary>結合後のメッシュ全体へ常に自動展開で生成する</summary>
        GenerateAll,
        /// <summary>
        /// 既存のUV2（手動展開・モデルインポータの自動展開どちらも）を保持し、
        /// UV2を持たないメッシュだけを自動展開した上で、全体を1枚のレイアウトに再パックする
        /// </summary>
        PreserveAndRepack,
    }

    /// <summary>
    /// 複数のRenderer（SkinnedMeshRenderer/MeshRenderer）を1つの静的メッシュにベイクするための設定コンポーネント。
    /// 設定はこのGameObjectにシリアライズされて保存され、ベイク自体は非破壊
    /// （成果物のMesh/Texture/Materialは別アセットとして出力される）。
    /// 実際のベイク処理はEditor側（StaticMeshBaker）が行う。
    /// </summary>
    [AddComponentMenu("YozoLab/Mesh Bake Group")]
    [DisallowMultipleComponent]
    public class MeshBakeGroup : MonoBehaviour
    {
        [Tooltip("ベイク対象のグループ。グループごとに別の静的Mesh/プレハブとして出力されます。" +
                 "マテリアルとアトラステクスチャは全グループで共有されます。" +
                 "現在のポーズ・ブレンドシェイプの状態で焼き込まれます。")]
        public List<BakeRendererGroup> rendererGroups = new List<BakeRendererGroup>
        {
            new BakeRendererGroup(),
        };

        [Header("出力")]
        [Tooltip("成果物（Mesh/Texture/Material）の出力先フォルダ")]
        public string outputDirectory = "Assets/BakedMeshes";

        [Tooltip("成果物のベース名。空の場合はこのGameObjectの名前を使用")]
        public string outputName = "";

        [Tooltip("ベイク結果を表示するMeshRendererをこのGameObjectの子として生成・更新する")]
        public bool createSceneObject = true;

        [Tooltip("生成したGameObjectをstaticにする")]
        public bool markStatic = true;

        [Header("マテリアル統合")]
        [Tooltip("全マテリアルをテクスチャアトラス化して1つのマテリアルに統合する")]
        public bool mergeMaterials = true;

        [Tooltip("マテリアルの扱い方。UnityStandard（標準）/ AutodeskInteractive / Filamented / Mochie を選ぶと、" +
                 "そのシェーダーのテクスチャプロパティを自動で収集し、対応するシェーダーのマテリアルとして統合します。" +
                 "Customでは Texture Properties の手動指定を使います。")]
        public MaterialMode materialMode = MaterialMode.UnityStandard;

        [Tooltip("統合マテリアルのベースにするマテリアル（シェーダーやスカラー系プロパティを引き継ぐ）。" +
                 "未設定なら最初に見つかったマテリアルを使用します。")]
        public Material mergeBaseMaterial;

        [Tooltip("アトラスの最大サイズ(px)")]
        public int atlasSize = 2048;

        [Tooltip("アトラス内の各テクスチャ間の余白(px)")]
        public int atlasPadding = 4;

        [Tooltip("アトラスサイズに応じてPaddingを自動調整する（atlasPaddingを2048px基準として比例スケール）。" +
                 "大きいアトラスでもミップマップの低位レベルで滲みにくくなります。")]
        public bool mipAwarePadding = true;

        [Tooltip("全テクスチャが目標密度のまま収まる場合、アトラスをより小さいサイズへ自動縮小します" +
                 "（VRAM節約。テクセル密度はほぼ低下しません）。UV Islandsパッキング時のみ有効です。")]
        public bool autoShrinkAtlas = true;

        [Tooltip("アトラス化するテクスチャプロパティ。先頭がレイアウトの基準になります。")]
        public List<string> textureProperties = new List<string> { "_MainTex" };

        [Tooltip("アトラス化の方式。UVIslandsはUVアイランド単位で詰め直すため充填率が高くなります。")]
        public AtlasPackingMode packingMode = AtlasPackingMode.UVIslands;

        [Tooltip("テクスチャごとの解像度の上書き（非破壊）。マテリアル検査UIから設定します。")]
        public List<TextureResolutionSetting> textureResolutionSettings = new List<TextureResolutionSetting>();

        [Header("UV最適化")]
        [Tooltip("実際に使用されているUV範囲だけを切り出してアトラスに詰める（アトラス領域の節約）")]
        public bool optimizeUVBounds = true;

        [Tooltip("マテリアルのTiling/OffsetをUVに焼き込む")]
        public bool bakeTextureST = true;

        [Tooltip("ソースUV上で重なり合うUVアイランド（ミラー/スタックなど意図的な重複や、" +
                 "複製メッシュの同一UV）を検知して、アトラスの同じ領域を共有させます。" +
                 "重複分のテクスチャが複製されなくなり、その分テクセル密度が向上します。" +
                 "（UV Islandsパッキング時のみ有効）")]
        public bool mergeOverlappingUVIslands = true;

        [Tooltip("ワールド表面積あたりのテクセル密度をアイランド間で均一化します。" +
                 "過剰に高密度なアイランドの領域を回収して全体に再配分します" +
                 "（各アイランドが元テクスチャの原寸密度を超えることはありません）。" +
                 "UV Islandsパッキング時のみ有効です。")]
        public bool normalizeTexelDensity = false;

        [Header("メッシュ最適化")]
        [Tooltip("不要な頂点属性を省略します: ノーマルマップを使わない出力では接線(Tangent)を、" +
                 "全頂点が白の場合は頂点カラーを出力しません（見た目は変わらずメモリを節約）。")]
        public bool stripUnusedVertexAttributes = true;

        [Tooltip("ライトマップ用UV2の扱い。\n" +
                 "None: UV2を出力しません。\n" +
                 "GenerateAll: 結合後のメッシュ全体へ常に自動展開で生成します。\n" +
                 "PreserveAndRepack: 既存のUV2（手動展開・インポータの自動展開）を保持し、" +
                 "UV2を持たないメッシュだけを自動展開した上で、" +
                 "ワールド表面積に応じたテクセル密度で全体を1枚のレイアウトに再パックします。")]
        public LightmapUVMode lightmapUVMode = LightmapUVMode.None;

        public string EffectiveOutputName =>
            string.IsNullOrEmpty(outputName) ? gameObject.name : outputName;

        /// <summary>テクスチャに設定された目標解像度（長辺px）。未設定なら0（原寸）。</summary>
        public int GetTargetResolution(Texture texture)
        {
            if (texture == null) return 0;
            foreach (TextureResolutionSetting setting in textureResolutionSettings)
            {
                if (setting != null && setting.texture == texture) return setting.targetResolution;
            }
            return 0;
        }

        /// <summary>
        /// アトラス上でのテクセル密度倍率。原寸=1。アップスケールはしない（最大1）。
        /// </summary>
        public float GetResolutionScale(Texture texture, int originalLongestEdge)
        {
            int target = GetTargetResolution(texture);
            if (target <= 0 || originalLongestEdge <= 0) return 1f;
            return Mathf.Clamp(target / (float)originalLongestEdge, 0.01f, 1f);
        }

        /// <summary>
        /// 実際に使うアトラスPadding(px)。mipAwarePadding有効時はatlasPaddingを
        /// 2048px基準としてアトラスサイズに比例させる（UV空間比を一定に保つ）。
        /// </summary>
        public int GetEffectiveAtlasPadding(int actualAtlasSize)
        {
            if (!mipAwarePadding) return atlasPadding;
            return Mathf.Clamp(Mathf.RoundToInt(atlasPadding * (actualAtlasSize / 2048f)), 2, 64);
        }

        /// <summary>
        /// ベイクに使う出力グループを解決する。
        /// 配列のrendererGroupsと、子のMeshBakeItemコンポーネントの両方を統合する。
        /// MeshBakeItemは明示リストに加えて、includeChildRenderers有効時は
        /// 配下のRendererを自動収集する（より深い階層の別グループが優先）。
        /// 同じRendererが複数のグループに重複して含まれる場合は先に現れたグループが優先され、
        /// 空のグループは除外される。
        /// </summary>
        public List<BakeRendererGroup> GetEffectiveGroups()
        {
            var result = new List<BakeRendererGroup>();
            var claimed = new HashSet<Renderer>();

            // 配列ベースのグループ（従来）
            if (rendererGroups != null)
            {
                foreach (BakeRendererGroup source in rendererGroups)
                {
                    if (source?.renderers == null) continue;
                    var group = new BakeRendererGroup { name = source.name };
                    foreach (Renderer renderer in source.renderers)
                    {
                        if (renderer == null || !claimed.Add(renderer)) continue;
                        group.renderers.Add(renderer);
                    }
                    if (group.renderers.Count > 0) result.Add(group);
                }
            }

            // 子のMeshBakeItemコンポーネント。
            // 全アイテムの明示リストを先に確定させてから自動収集する（明示指定が常に優先）。
            MeshBakeItem[] componentItems = GetComponentsInChildren<MeshBakeItem>(true);
            var resolvedGroups = new List<BakeRendererGroup>(componentItems.Length);
            for (int i = 0; i < componentItems.Length; i++)
            {
                var group = new BakeRendererGroup { name = componentItems[i].EffectiveName };
                foreach (Renderer renderer in componentItems[i].renderers)
                {
                    if (renderer == null || !claimed.Add(renderer)) continue;
                    group.renderers.Add(renderer);
                }
                resolvedGroups.Add(group);
            }
            for (int i = 0; i < componentItems.Length; i++)
            {
                MeshBakeItem item = componentItems[i];
                BakeRendererGroup group = resolvedGroups[i];
                if (item.includeChildRenderers)
                {
                    foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                        // ネストしたアイテム配下のRendererは近い方のアイテムに割り当てる
                        if (FindNearestItem(renderer.transform) != item) continue;
                        if (!claimed.Add(renderer)) continue;
                        group.renderers.Add(renderer);
                    }
                }
                if (group.renderers.Count > 0) result.Add(group);
            }

            return result;
        }

        /// <summary>Transformから最も近い祖先（自身含む）のMeshBakeItemを返す（非アクティブも対象）</summary>
        private static MeshBakeItem FindNearestItem(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                MeshBakeItem item = current.GetComponent<MeshBakeItem>();
                if (item != null) return item;
            }
            return null;
        }
    }
}
