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
    }

    /// <summary>アトラス化の方式</summary>
    public enum AtlasPackingMode
    {
        /// <summary>UVアイランド単位で詰め直す（充填率が高い。TexTransToolのNFDH+FCアルゴリズムを採用）</summary>
        UVIslands,
        /// <summary>マテリアルごとの矩形単位で詰める（単純・安全）</summary>
        MaterialRects,
    }

    /// <summary>
    /// 複数のRenderer（SkinnedMeshRenderer/MeshRenderer）を1つの静的メッシュにベイクするための設定コンポーネント。
    /// 設定はこのGameObjectにシリアライズされて保存され、ベイク自体は非破壊
    /// （成果物のMesh/Texture/Materialは別アセットとして出力される）。
    /// 実際のベイク処理はEditor側（StaticMeshBaker）が行う。
    /// </summary>
    [AddComponentMenu("YozoLab/Mesh Bake Assembly")]
    [DisallowMultipleComponent]
    public class MeshBakeAssembly : MonoBehaviour
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

        [Tooltip("マテリアルの扱い方。AutodeskInteractiveを選ぶと、対応するテクスチャプロパティを自動で収集し、" +
                 "Autodesk Interactiveシェーダーのマテリアルとして確実に統合します。")]
        public MaterialMode materialMode = MaterialMode.Custom;

        [Tooltip("統合マテリアルのベースにするマテリアル（シェーダーやスカラー系プロパティを引き継ぐ）。" +
                 "未設定なら最初に見つかったマテリアルを使用します。")]
        public Material mergeBaseMaterial;

        [Tooltip("アトラスの最大サイズ(px)")]
        public int atlasSize = 2048;

        [Tooltip("アトラス内の各テクスチャ間の余白(px)")]
        public int atlasPadding = 4;

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

        [Tooltip("ライトマップ用のUV2を生成する")]
        public bool generateLightmapUVs = false;

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
    }
}
