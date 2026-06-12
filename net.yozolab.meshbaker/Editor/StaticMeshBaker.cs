using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    public class BakeReport
    {
        public readonly List<string> meshPaths = new List<string>();
        public readonly List<string> prefabPaths = new List<string>();
        public string materialPath;
        public int vertexCount;
        public int sourceMaterialCount;
        public int submeshCount;
        public readonly List<string> warnings = new List<string>();
        /// <summary>警告ではない補足情報（最適化の実施内容など）</summary>
        public readonly List<string> infos = new List<string>();
    }

    /// <summary>1つのSkinnedMeshRendererの1サブメッシュ分のベイク済みデータ</summary>
    internal class BakePart
    {
        /// <summary>引き継ぐ追加UVチャンネル数（UV3〜UV8 = メッシュチャンネル2〜7）</summary>
        internal const int ExtraUVChannels = 6;

        public Material material;
        /// <summary>所属する出力グループ（GetEffectiveGroupsで解決されたグループのインデックス）</summary>
        public int groupIndex;
        public Vector3[] positions;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Vector2[] uv;
        /// <summary>ライトマップ用UV2（PreserveAndRepackモード時のみ抽出。元メッシュに無い場合null）</summary>
        public Vector2[] uv2;
        /// <summary>UV3〜UV8（インデックス0=UV3）。元メッシュに無いチャンネルはnull。2D成分のみ引き継ぐ。</summary>
        public Vector2[][] extraUVs = new Vector2[ExtraUVChannels][];
        public Color32[] colors;
        public int[] indices;
    }

    /// <summary>同一マテリアルのBakePartをまとめたグループ（アトラス化の単位）</summary>
    internal class BakeMaterialGroup
    {
        public Material material;
        public readonly List<BakePart> parts = new List<BakePart>();
        public Rect uvBounds = new Rect(0, 0, 1, 1);
    }

    /// <summary>
    /// MeshBakeAssemblyの設定に従って、複数のSkinnedMeshRendererを
    /// 現在のポーズで1つの静的メッシュにベイクし、別アセットとして出力する。
    /// </summary>
    public static class StaticMeshBaker
    {
        public static BakeReport Bake(MeshBakeAssembly assembly)
        {
            var report = new BakeReport();
            List<BakeRendererGroup> effectiveGroups = assembly.GetEffectiveGroups();
            List<BakePart> parts = ExtractParts(assembly, effectiveGroups, report);
            if (parts.Count == 0)
            {
                throw new InvalidOperationException(
                    "ベイク対象が見つかりませんでした。Renderer Groupsまたは子のMesh Bake Groupに" +
                    "Renderer（SkinnedMeshRenderer/MeshRenderer）を設定してください。");
            }

            var distinctMaterials = new List<Material>();
            foreach (BakePart part in parts)
            {
                if (!distinctMaterials.Contains(part.material)) distinctMaterials.Add(part.material);
            }
            report.sourceMaterialCount = distinctMaterials.Count;

            // ---- マテリアル統合（全出力グループで共有のアトラス/マテリアルを作る） ----
            // マテリアルモードに応じてアトラス対象のテクスチャプロパティを決定する。
            // 先頭（メイン）は常に含め、それ以外はどれかのマテリアルが実際に持つものだけに絞る
            // （未使用のマップで空アトラス/誤キーワードが生まれるのを防ぐ）。
            string[] candidateProperties =
                MaterialModeProfiles.GetCollectProperties(assembly.materialMode, assembly.textureProperties);
            string mainProperty = candidateProperties[0];
            var effectiveList = new List<string> { mainProperty };
            for (int i = 1; i < candidateProperties.Length; i++)
            {
                string prop = candidateProperties[i];
                bool present = distinctMaterials.Any(m =>
                    m.HasProperty(prop) && m.GetTexture(prop) != null);
                if (present) effectiveList.Add(prop);
            }
            string[] effectiveProperties = effectiveList.ToArray();

            // Tiling/Offsetの焼き込みはパート単位で行う（同一テクスチャ・別STのマテリアルを
            // 後段のテクスチャ構成グループ化で正しく統合できるようにするため）
            if (assembly.mergeMaterials && assembly.bakeTextureST)
            {
                BakeTextureSTPerPart(parts, mainProperty);
            }

            // 統合時はマテリアル参照ではなくテクスチャ構成でグループ化し、
            // 同じテクスチャ一式を参照するマテリアル同士でアトラス領域を共有する
            List<BakeMaterialGroup> materialGroups =
                GroupParts(parts, assembly.mergeMaterials, effectiveProperties);
            if (assembly.mergeMaterials && materialGroups.Count < distinctMaterials.Count)
            {
                report.infos.Add($"同一テクスチャ構成のマテリアルを統合: " +
                                 $"{distinctMaterials.Count}マテリアル → {materialGroups.Count}アトラス領域");
            }

            MaterialAtlasBuilder.Result atlasResult = null;
            if (assembly.mergeMaterials)
            {
                PrepareUVsForAtlas(assembly, materialGroups, report);

                if (assembly.packingMode == AtlasPackingMode.UVIslands)
                {
                    // UVアイランド単位の詰め直し（UV書き換えも内部で行われる）
                    atlasResult = UVIslandAtlasPacker.Pack(
                        assembly, materialGroups, effectiveProperties,
                        assembly.atlasSize, report.warnings, report.infos);
                }
                else
                {
                    List<float> resolutionScales = materialGroups.Select(g =>
                    {
                        Texture tex = g.material != null && g.material.HasProperty(mainProperty)
                            ? g.material.GetTexture(mainProperty) : null;
                        int longest = tex != null ? Mathf.Max(tex.width, tex.height) : 0;
                        return assembly.GetResolutionScale(tex, longest);
                    }).ToList();

                    atlasResult = MaterialAtlasBuilder.Build(
                        materialGroups.Select(g => g.material).ToList(),
                        materialGroups.Select(g => g.uvBounds).ToList(),
                        resolutionScales,
                        effectiveProperties, assembly.atlasSize,
                        assembly.GetEffectiveAtlasPadding(assembly.atlasSize),
                        report.warnings);

                    for (int i = 0; i < materialGroups.Count; i++)
                    {
                        RemapGroupUVs(materialGroups[i], atlasResult.rects[i]);
                    }
                }
            }

            string directory = assembly.outputDirectory.TrimEnd('/');
            EnsureFolder(directory);
            string baseName = assembly.EffectiveOutputName;

            Material[] sharedMaterials = null;
            if (assembly.mergeMaterials)
            {
                // 統合マテリアルのベース: 明示指定があればそれを、なければ先頭マテリアルを使う
                Material baseMaterial = assembly.mergeBaseMaterial != null
                    ? assembly.mergeBaseMaterial
                    : materialGroups[0].material;
                Material merged = BuildMergedMaterial(assembly, baseMaterial, atlasResult, directory, baseName, report);
                report.materialPath = $"{directory}/{baseName}_mat.mat";
                sharedMaterials = new[] { SaveMaterialAsset(merged, report.materialPath) };

                foreach (Texture2D atlas in atlasResult.atlases.Values)
                {
                    UnityEngine.Object.DestroyImmediate(atlas);
                }
            }

            // ---- 出力グループごとにMesh/プレハブを出力する ----
            var combineStats = new CombineStats();
            for (int groupIndex = 0; groupIndex < effectiveGroups.Count; groupIndex++)
            {
                var submeshes = new List<List<BakePart>>();
                var materials = new List<Material>();

                if (assembly.mergeMaterials)
                {
                    List<BakePart> groupParts = parts.Where(p => p.groupIndex == groupIndex).ToList();
                    if (groupParts.Count > 0)
                    {
                        submeshes.Add(groupParts);
                        materials.AddRange(sharedMaterials);
                    }
                }
                else
                {
                    foreach (BakeMaterialGroup materialGroup in materialGroups)
                    {
                        List<BakePart> groupParts = materialGroup.parts
                            .Where(p => p.groupIndex == groupIndex).ToList();
                        if (groupParts.Count == 0) continue;
                        submeshes.Add(groupParts);
                        materials.Add(materialGroup.material);
                    }
                }

                if (submeshes.Count == 0)
                {
                    report.warnings.Add($"グループ{groupIndex + 1}: ベイク対象がないためスキップしました。");
                    continue;
                }

                if (assembly.lightmapUVMode == LightmapUVMode.PreserveAndRepack)
                {
                    // 既存UV2の保持と再パック（頂点分割が起きうるため結合前に行う）
                    LightmapUVPacker.PreserveAndRepack(
                        submeshes.SelectMany(s => s).ToList(), report.warnings);
                }

                // ノーマルマップを使わない出力では接線を省略できる
                bool keepTangents = !assembly.stripUnusedVertexAttributes ||
                                    materials.Any(UsesNormalMap);
                Mesh combined = BuildCombinedMesh(
                    submeshes, keepTangents, assembly.stripUnusedVertexAttributes, combineStats);
                if (assembly.lightmapUVMode == LightmapUVMode.GenerateAll)
                {
                    Unwrapping.GenerateSecondaryUVSet(combined);
                }
                MeshUtility.Optimize(combined); // 頂点キャッシュ・フェッチ局所性の最適化
                report.vertexCount += combined.vertexCount;
                report.submeshCount += submeshes.Count;

                string suffix = GetGroupSuffix(effectiveGroups, groupIndex);
                string meshPath = $"{directory}/{baseName}{suffix}_mesh.asset";
                Mesh savedMesh = SaveMeshAsset(combined, meshPath);
                report.meshPaths.Add(meshPath);

                if (assembly.createSceneObject)
                {
                    report.prefabPaths.Add(UpdateScenePrefab(
                        assembly, savedMesh, materials.ToArray(), directory, $"{baseName}{suffix}_Baked"));
                }
            }

            if (report.meshPaths.Count == 0)
            {
                throw new InvalidOperationException("出力できるグループがありませんでした。");
            }

            if (combineStats.weldedTo < combineStats.weldedFrom)
            {
                report.infos.Add($"頂点溶接: {combineStats.weldedFrom} → {combineStats.weldedTo} 頂点");
            }
            if (combineStats.degenerateTriangles > 0)
            {
                report.infos.Add($"退化三角形を{combineStats.degenerateTriangles}個除去しました。");
            }
            if (combineStats.strippedTangents)
            {
                report.infos.Add("ノーマルマップ未使用のため接線(Tangent)を省略しました。");
            }
            if (combineStats.strippedColors)
            {
                report.infos.Add("頂点カラーが全て白のため省略しました。");
            }

            AssetDatabase.SaveAssets();
            return report;
        }

        /// <summary>成果物アセット名のグループサフィックス。単一の無名グループなら付けない（従来互換）</summary>
        private static string GetGroupSuffix(List<BakeRendererGroup> groups, int groupIndex)
        {
            BakeRendererGroup group = groups[groupIndex];
            if (groups.Count == 1 && string.IsNullOrEmpty(group.name)) return "";
            return "_" + (string.IsNullOrEmpty(group.name) ? $"Group{groupIndex + 1}" : group.name);
        }

        // ---------------------------------------------------------------
        // ベイクと抽出
        // ---------------------------------------------------------------

        private static List<BakePart> ExtractParts(
            MeshBakeAssembly assembly, List<BakeRendererGroup> groups, BakeReport report)
        {
            var parts = new List<BakePart>();
            Transform root = assembly.transform;

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            foreach (Renderer renderer in groups[groupIndex].renderers)
            {
                Mesh source = GetSharedMesh(renderer);
                if (renderer == null || source == null)
                {
                    if (renderer != null) report.warnings.Add($"{renderer.name}: メッシュがないためスキップしました。");
                    continue;
                }

                // 位置/法線/接線とアセンブリローカルへの変換行列を、レンダラー種別ごとに求める
                Vector3[] bakedPositions;
                Vector3[] bakedNormals;
                Vector4[] bakedTangents;
                Matrix4x4 toRoot;
                Mesh tempBaked = null;

                if (renderer is SkinnedMeshRenderer smr)
                {
                    tempBaked = new Mesh();
                    smr.BakeMesh(tempBaked, true); // スケール込み・現在のポーズで焼く
                    bakedPositions = tempBaked.vertices;
                    bakedNormals = tempBaked.normals;
                    bakedTangents = tempBaked.tangents;
                    // BakeMeshの結果はレンダラーのローカル空間（スケール適用済み）なので、
                    // 回転と平行移動だけでアセンブリのローカル空間へ変換する
                    toRoot = root.worldToLocalMatrix *
                             Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                }
                else
                {
                    // 通常のMeshRenderer: メッシュはローカル空間のまま。
                    // レンダラーのlocalToWorld（スケール込み）でアセンブリローカルへ変換する。
                    bakedPositions = source.vertices;
                    bakedNormals = source.normals;
                    bakedTangents = source.tangents;
                    toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                }

                Vector2[] sourceUV = source.uv;
                // 既存UV2の保持が要るモードのときだけ抽出する（手動展開・インポータの自動展開どちらも対象）
                Vector2[] sourceUV2 = assembly.lightmapUVMode == LightmapUVMode.PreserveAndRepack
                    ? source.uv2
                    : Array.Empty<Vector2>();
                // UV3〜UV8はシェーダーのカスタムデータとして使われている可能性があるため常に引き継ぐ
                var sourceExtraUVs = new Vector2[BakePart.ExtraUVChannels][];
                var channelBuffer = new List<Vector2>();
                for (int ch = 0; ch < BakePart.ExtraUVChannels; ch++)
                {
                    source.GetUVs(ch + 2, channelBuffer);
                    sourceExtraUVs[ch] = channelBuffer.Count > 0 ? channelBuffer.ToArray() : null;
                }
                Color32[] sourceColors = source.colors32;
                Material[] materials = renderer.sharedMaterials;

                for (int s = 0; s < source.subMeshCount; s++)
                {
                    Material material = materials.Length > 0 ? materials[Mathf.Min(s, materials.Length - 1)] : null;
                    if (material == null)
                    {
                        report.warnings.Add($"{renderer.name}: サブメッシュ{s}のマテリアルが未設定のためスキップしました。");
                        continue;
                    }

                    BakePart part = ExtractSubmesh(
                        source.GetTriangles(s), bakedPositions, bakedNormals, bakedTangents,
                        sourceUV, sourceUV2, sourceExtraUVs, sourceColors, toRoot);
                    part.material = material;
                    part.groupIndex = groupIndex;
                    parts.Add(part);
                }

                if (tempBaked != null) UnityEngine.Object.DestroyImmediate(tempBaked);
            }

            return parts;
        }

        /// <summary>SkinnedMeshRenderer / MeshRenderer どちらからもメッシュを取得する</summary>
        internal static Mesh GetSharedMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (renderer != null)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null) return filter.sharedMesh;
            }
            return null;
        }

        private static BakePart ExtractSubmesh(
            int[] triangles, Vector3[] positions, Vector3[] normals, Vector4[] tangents,
            Vector2[] uv, Vector2[] uv2, Vector2[][] extraUVs, Color32[] colors, Matrix4x4 toRoot)
        {
            var map = new Dictionary<int, int>();
            var part = new BakePart { indices = new int[triangles.Length] };
            var outPositions = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outTangents = new List<Vector4>();
            var outUV = new List<Vector2>();
            var outUV2 = uv2.Length > 0 ? new List<Vector2>() : null;
            var outExtraUVs = new List<Vector2>[BakePart.ExtraUVChannels];
            for (int ch = 0; ch < BakePart.ExtraUVChannels; ch++)
            {
                if (extraUVs[ch] != null) outExtraUVs[ch] = new List<Vector2>();
            }
            var outColors = colors.Length > 0 ? new List<Color32>() : null;

            for (int i = 0; i < triangles.Length; i++)
            {
                int sourceIndex = triangles[i];
                if (!map.TryGetValue(sourceIndex, out int newIndex))
                {
                    newIndex = outPositions.Count;
                    map.Add(sourceIndex, newIndex);

                    outPositions.Add(toRoot.MultiplyPoint3x4(positions[sourceIndex]));
                    outNormals.Add(normals.Length > 0
                        ? toRoot.MultiplyVector(normals[sourceIndex]).normalized
                        : Vector3.up);
                    if (tangents.Length > 0)
                    {
                        Vector3 t = toRoot.MultiplyVector(tangents[sourceIndex]).normalized;
                        outTangents.Add(new Vector4(t.x, t.y, t.z, tangents[sourceIndex].w));
                    }
                    else
                    {
                        outTangents.Add(new Vector4(1, 0, 0, -1));
                    }
                    outUV.Add(uv.Length > 0 ? uv[sourceIndex] : Vector2.zero);
                    outUV2?.Add(uv2[sourceIndex]);
                    for (int ch = 0; ch < BakePart.ExtraUVChannels; ch++)
                    {
                        outExtraUVs[ch]?.Add(extraUVs[ch][sourceIndex]);
                    }
                    outColors?.Add(colors[sourceIndex]);
                }
                part.indices[i] = newIndex;
            }

            part.positions = outPositions.ToArray();
            part.normals = outNormals.ToArray();
            part.tangents = outTangents.ToArray();
            part.uv = outUV.ToArray();
            part.uv2 = outUV2?.ToArray();
            for (int ch = 0; ch < BakePart.ExtraUVChannels; ch++)
            {
                part.extraUVs[ch] = outExtraUVs[ch]?.ToArray();
            }
            part.colors = outColors?.ToArray();
            return part;
        }

        /// <summary>
        /// パートをアトラス化の単位にグループ分けする。
        /// マテリアル統合時はテクスチャ構成（アトラス対象プロパティのテクスチャ一式）でグループ化し、
        /// 同じテクスチャを参照するだけの別マテリアルが同じ絵をアトラスへ二重に焼き込まないようにする。
        /// 統合しない場合は出力サブメッシュが元マテリアルを参照するため、マテリアル参照でグループ化する。
        /// </summary>
        private static List<BakeMaterialGroup> GroupParts(
            List<BakePart> parts, bool byTextureSet, IReadOnlyList<string> properties)
        {
            var groups = new List<BakeMaterialGroup>();
            var byKey = new Dictionary<string, BakeMaterialGroup>();
            foreach (BakePart part in parts)
            {
                string key = byTextureSet
                    ? TextureSetKey(part.material, properties)
                    : "mat:" + part.material.GetInstanceID();
                if (!byKey.TryGetValue(key, out BakeMaterialGroup group))
                {
                    group = new BakeMaterialGroup { material = part.material };
                    byKey.Add(key, group);
                    groups.Add(group);
                }
                group.parts.Add(part);
            }
            return groups;
        }

        private static string TextureSetKey(Material material, IReadOnlyList<string> properties)
        {
            var key = new System.Text.StringBuilder();
            foreach (string property in properties)
            {
                Texture texture = material.HasProperty(property) ? material.GetTexture(property) : null;
                key.Append(texture != null ? texture.GetInstanceID() : 0).Append(';');
            }
            // メインテクスチャを持たないマテリアルは_Colorの単色で塗られるため、色もキーに含める
            Texture main = material.HasProperty(properties[0]) ? material.GetTexture(properties[0]) : null;
            if (main == null)
            {
                Color color = material.HasProperty("_Color") ? material.color : Color.white;
                key.Append("c:").Append(color.r).Append(',').Append(color.g)
                   .Append(',').Append(color.b).Append(',').Append(color.a);
            }
            return key.ToString();
        }

        /// <summary>
        /// マテリアルのTiling/Offsetを各パートのUVへ焼き込む。
        /// グループ化の前にパート単位で行うことで、同一テクスチャ・別STのマテリアル同士も
        /// 焼き込み後は同じテクスチャ空間を指し、1つのアトラス領域を共有できる。
        /// </summary>
        private static void BakeTextureSTPerPart(List<BakePart> parts, string mainProperty)
        {
            foreach (BakePart part in parts)
            {
                if (!part.material.HasProperty(mainProperty)) continue;
                Vector2 scale = part.material.GetTextureScale(mainProperty);
                Vector2 offset = part.material.GetTextureOffset(mainProperty);
                if (scale == Vector2.one && offset == Vector2.zero) continue;
                for (int i = 0; i < part.uv.Length; i++)
                {
                    part.uv[i] = Vector2.Scale(part.uv[i], scale) + offset;
                }
            }
        }

        /// <summary>マテリアルがノーマルマップ系のテクスチャを実際に持つか（接線の要否判定）</summary>
        private static bool UsesNormalMap(Material material)
        {
            if (material == null) return false;
            foreach (string property in material.GetTexturePropertyNames())
            {
                if (MaterialAtlasBuilder.IsNormalProperty(property) &&
                    material.GetTexture(property) != null)
                {
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------
        // UV処理
        // ---------------------------------------------------------------

        /// <summary>
        /// アトラス化に向けて各グループのUVを整える。
        /// 整数オフセットの除去 → 使用UV範囲の計算を行う
        /// （Tiling/Offsetの焼き込みはグループ化前にBakeTextureSTPerPartで実施済み）。
        /// 使用範囲が[0,1]を超える場合はタイリングごと切り出される（解像度低下に注意）。
        /// </summary>
        private static void PrepareUVsForAtlas(
            MeshBakeAssembly assembly, List<BakeMaterialGroup> groups, BakeReport report)
        {
            foreach (BakeMaterialGroup group in groups)
            {
                // 使用UV範囲を求め、整数分のオフセットを取り除く
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (BakePart part in group.parts)
                {
                    foreach (Vector2 uv in part.uv)
                    {
                        min = Vector2.Min(min, uv);
                        max = Vector2.Max(max, uv);
                    }
                }
                var shift = new Vector2(Mathf.Floor(min.x), Mathf.Floor(min.y));
                if (shift != Vector2.zero)
                {
                    foreach (BakePart part in group.parts)
                    {
                        for (int i = 0; i < part.uv.Length; i++) part.uv[i] -= shift;
                    }
                    min -= shift;
                    max -= shift;
                }

                Vector2 size = max - min;
                if (size.x > 4f || size.y > 4f)
                {
                    report.warnings.Add(
                        $"{group.material.name}: UVが広範囲にタイリングされています（{size.x:F1} x {size.y:F1}）。" +
                        "タイリングごとアトラスに焼き込むため、解像度が大きく低下します。");
                }

                if (assembly.optimizeUVBounds)
                {
                    // バイリニア滲み対策に少しだけ広げて切り出す
                    Vector2 margin = size * 0.005f + new Vector2(0.002f, 0.002f);
                    min -= margin;
                    max += margin;
                    group.uvBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                }
                else
                {
                    // 縮小はしないが、[0,1]を超える分はカバーする
                    group.uvBounds = Rect.MinMaxRect(
                        Mathf.Min(0f, min.x), Mathf.Min(0f, min.y),
                        Mathf.Max(1f, max.x), Mathf.Max(1f, max.y));
                }
            }
        }

        private static void RemapGroupUVs(BakeMaterialGroup group, Rect atlasRect)
        {
            Rect bounds = group.uvBounds;
            foreach (BakePart part in group.parts)
            {
                for (int i = 0; i < part.uv.Length; i++)
                {
                    var normalized = new Vector2(
                        (part.uv[i].x - bounds.x) / bounds.width,
                        (part.uv[i].y - bounds.y) / bounds.height);
                    part.uv[i] = atlasRect.position + Vector2.Scale(normalized, atlasRect.size);
                }
            }
        }

        // ---------------------------------------------------------------
        // メッシュ結合
        // ---------------------------------------------------------------

        /// <summary>メッシュ結合時の最適化の統計（全グループ累計）</summary>
        private class CombineStats
        {
            public int weldedFrom;
            public int weldedTo;
            public int degenerateTriangles;
            public bool strippedTangents;
            public bool strippedColors;
        }

        /// <summary>
        /// サブメッシュごとのBakePartリストから1つのメッシュを構築する。
        /// 全属性が一致する頂点の溶接（サブメッシュ境界で複製された頂点の除去）、
        /// 退化三角形の除去、不要な頂点属性の省略も行う。
        /// </summary>
        private static Mesh BuildCombinedMesh(
            List<List<BakePart>> submeshes, bool keepTangents, bool stripUnused, CombineStats stats)
        {
            int submeshCount = submeshes.Count;
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv = new List<Vector2>();
            var uv2 = new List<Vector2>();
            var colors = new List<Color32>();
            bool anyColors = submeshes.Any(s => s.Any(p => p.colors != null));
            bool anyUV2 = submeshes.Any(s => s.Any(p => p.uv2 != null && p.uv2.Length > 0));
            var extraUV = new List<Vector2>[BakePart.ExtraUVChannels];
            for (int ch = 0; ch < BakePart.ExtraUVChannels; ch++)
            {
                int channel = ch;
                if (submeshes.Any(s => s.Any(p => p.extraUVs[channel] != null)))
                {
                    extraUV[ch] = new List<Vector2>();
                }
            }
            var submeshIndices = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++) submeshIndices[i] = new List<int>();

            for (int s = 0; s < submeshCount; s++)
            {
                List<int> indices = submeshIndices[s];
                foreach (BakePart part in submeshes[s])
                {
                    int offset = positions.Count;
                    positions.AddRange(part.positions);
                    normals.AddRange(part.normals);
                    tangents.AddRange(part.tangents);
                    uv.AddRange(part.uv);
                    if (anyUV2)
                    {
                        if (part.uv2 != null && part.uv2.Length == part.positions.Length) uv2.AddRange(part.uv2);
                        else for (int i = 0; i < part.positions.Length; i++) uv2.Add(Vector2.zero);
                    }
                    for (int ch = 0; ch < BakePart.ExtraUVChannels; ch++)
                    {
                        if (extraUV[ch] == null) continue;
                        if (part.extraUVs[ch] != null) extraUV[ch].AddRange(part.extraUVs[ch]);
                        else for (int i = 0; i < part.positions.Length; i++) extraUV[ch].Add(Vector2.zero);
                    }
                    if (anyColors)
                    {
                        if (part.colors != null) colors.AddRange(part.colors);
                        else for (int i = 0; i < part.positions.Length; i++) colors.Add(new Color32(255, 255, 255, 255));
                    }
                    foreach (int index in part.indices) indices.Add(index + offset);
                }
            }

            // 全頂点が白の頂点カラーは省略する（チャンネルなしと描画上等価）
            if (stripUnused && anyColors &&
                colors.All(c => c.r == 255 && c.g == 255 && c.b == 255 && c.a == 255))
            {
                anyColors = false;
                stats.strippedColors = true;
            }
            if (!keepTangents) stats.strippedTangents = true;

            // 完全一致頂点の溶接。省略する属性は比較から外して溶接の機会を増やす
            stats.weldedFrom += positions.Count;
            int[] remap = WeldVertices(
                positions, normals, keepTangents ? tangents : null, uv,
                anyUV2 ? uv2 : null, anyColors ? colors : null, extraUV);
            stats.weldedTo += positions.Count;

            var mesh = new Mesh
            {
                indexFormat = positions.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            if (keepTangents) mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv);
            if (anyUV2) mesh.SetUVs(1, uv2); // チャンネル1 = Mesh.uv2（ライトマップUV）
            for (int ch = 0; ch < BakePart.ExtraUVChannels; ch++)
            {
                if (extraUV[ch] != null) mesh.SetUVs(ch + 2, extraUV[ch]);
            }
            if (anyColors) mesh.SetColors(colors);

            mesh.subMeshCount = submeshCount;
            for (int s = 0; s < submeshCount; s++)
            {
                List<int> indices = submeshIndices[s];
                var remapped = new List<int>(indices.Count);
                for (int i = 0; i < indices.Count; i += 3)
                {
                    int a = remap[indices[i]];
                    int b = remap[indices[i + 1]];
                    int c = remap[indices[i + 2]];
                    // 溶接で潰れた退化三角形（ゼロスケールのベイク結果など）は捨てる
                    if (a == b || b == c || c == a)
                    {
                        stats.degenerateTriangles++;
                        continue;
                    }
                    remapped.Add(a);
                    remapped.Add(b);
                    remapped.Add(c);
                }
                mesh.SetTriangles(remapped, s);
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 全属性が完全一致（ビット等価）する頂点を溶接し、各リストを先頭詰めで圧縮して
        /// 旧インデックス→新インデックスの対応表を返す。
        /// 完全一致のみを対象とするため、ハードエッジ（法線分割）やUVシームは保持される。
        /// </summary>
        private static int[] WeldVertices(
            List<Vector3> positions, List<Vector3> normals, List<Vector4> tangents,
            List<Vector2> uv, List<Vector2> uv2, List<Color32> colors, List<Vector2>[] extraUV)
        {
            var comparer = new VertexComparer(positions, normals, tangents, uv, uv2, colors, extraUV);
            var firstIndex = new Dictionary<int, int>(positions.Count, comparer);
            var remap = new int[positions.Count];
            var keep = new List<int>(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                if (firstIndex.TryGetValue(i, out int compact))
                {
                    remap[i] = compact;
                    continue;
                }
                compact = keep.Count;
                firstIndex.Add(i, compact);
                keep.Add(i);
                remap[i] = compact;
            }
            if (keep.Count == positions.Count) return remap; // 重複なし

            CompactList(positions, keep);
            CompactList(normals, keep);
            CompactList(tangents, keep);
            CompactList(uv, keep);
            CompactList(uv2, keep);
            CompactList(colors, keep);
            foreach (List<Vector2> channel in extraUV) CompactList(channel, keep);
            return remap;
        }

        /// <summary>keepに列挙された旧インデックスの要素だけを先頭詰めで残す（keepは昇順）</summary>
        private static void CompactList<T>(List<T> list, List<int> keep)
        {
            if (list == null || list.Count == 0) return;
            for (int i = 0; i < keep.Count; i++) list[i] = list[keep[i]];
            list.RemoveRange(keep.Count, list.Count - keep.Count);
        }

        /// <summary>頂点インデックスを全属性のビット等価で比較するComparer（溶接用）。null属性は比較しない。</summary>
        private class VertexComparer : IEqualityComparer<int>
        {
            private readonly List<Vector3> positions;
            private readonly List<Vector3> normals;
            private readonly List<Vector4> tangents;
            private readonly List<Vector2> uv;
            private readonly List<Vector2> uv2;
            private readonly List<Color32> colors;
            private readonly List<Vector2>[] extraUV;

            public VertexComparer(
                List<Vector3> positions, List<Vector3> normals, List<Vector4> tangents,
                List<Vector2> uv, List<Vector2> uv2, List<Color32> colors, List<Vector2>[] extraUV)
            {
                this.positions = positions;
                this.normals = normals;
                this.tangents = tangents;
                this.uv = uv;
                this.uv2 = uv2;
                this.colors = colors;
                this.extraUV = extraUV;
            }

            public bool Equals(int a, int b)
            {
                if (!Same(positions[a], positions[b])) return false;
                if (!Same(normals[a], normals[b])) return false;
                if (tangents != null && !Same(tangents[a], tangents[b])) return false;
                if (!Same(uv[a], uv[b])) return false;
                if (uv2 != null && !Same(uv2[a], uv2[b])) return false;
                if (colors != null && !Same(colors[a], colors[b])) return false;
                foreach (List<Vector2> channel in extraUV)
                {
                    if (channel != null && !Same(channel[a], channel[b])) return false;
                }
                return true;
            }

            public int GetHashCode(int i)
            {
                // 位置とUVだけでハッシュし、残りはEqualsで厳密比較する
                unchecked
                {
                    Vector3 p = positions[i];
                    Vector2 t = uv[i];
                    int h = p.x.GetHashCode();
                    h = (h * 397) ^ p.y.GetHashCode();
                    h = (h * 397) ^ p.z.GetHashCode();
                    h = (h * 397) ^ t.x.GetHashCode();
                    h = (h * 397) ^ t.y.GetHashCode();
                    return h;
                }
            }

            // Vector同士の==演算子は近似比較のため、ハッシュと整合するビット等価で比較する
            private static bool Same(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
            private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
            private static bool Same(Vector4 a, Vector4 b) =>
                a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
            private static bool Same(Color32 a, Color32 b) =>
                a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        // ---------------------------------------------------------------
        // アセット保存
        // ---------------------------------------------------------------

        /// <summary>
        /// アトラス結果を1つの統合マテリアルにまとめる。
        /// 各テクスチャプロパティのアトラスを同名プロパティへ割り当てる。
        /// AutodeskInteractiveモードでは、確実にAutodesk Interactiveシェーダーのマテリアルとして出力し、
        /// 割り当てたマップに対応するUse*トグルを有効化する。
        /// </summary>
        private static Material BuildMergedMaterial(
            MeshBakeAssembly assembly, Material baseMaterial,
            MaterialAtlasBuilder.Result atlasResult, string directory, string baseName, BakeReport report)
        {
            // ベースマテリアルを複製してシェーダー・スカラー系のプロパティを引き継ぐ
            var merged = new Material(baseMaterial);

            bool aiMode = assembly.materialMode == MaterialMode.AutodeskInteractive;
            if (aiMode)
            {
                EnsureAutodeskInteractiveShader(merged, report);
            }

            foreach (KeyValuePair<string, Texture2D> entry in atlasResult.atlases)
            {
                string texturePath = $"{directory}/{baseName}{entry.Key}.png";
                // 自動縮小でアトラスがassembly.atlasSizeより小さいことがあるため、実サイズを使う
                Texture2D savedTexture = SaveTextureAsset(
                    entry.Value, texturePath,
                    Mathf.Max(entry.Value.width, entry.Value.height),
                    MaterialAtlasBuilder.IsNormalProperty(entry.Key),
                    MaterialAtlasBuilder.IsLinearProperty(entry.Key));
                if (merged.HasProperty(entry.Key))
                {
                    merged.SetTexture(entry.Key, savedTexture);
                    merged.SetTextureScale(entry.Key, Vector2.one);
                    merged.SetTextureOffset(entry.Key, Vector2.zero);
                }

                if (aiMode) EnableAutodeskInteractiveMap(merged, entry.Key);
            }
            return merged;
        }

        /// <summary>割り当てたマップに対応するシェーダーキーワードを有効化する（発光はGI寄与も設定）</summary>
        private static void EnableAutodeskInteractiveMap(Material material, string textureProperty)
        {
            if (MaterialModeProfiles.AutodeskInteractiveKeywords.TryGetValue(textureProperty, out string keyword))
            {
                material.EnableKeyword(keyword);
            }

            if (textureProperty == MaterialModeProfiles.AI_Emission)
            {
                // 発光マップをライトベイク（GI）に寄与させる
                if (material.HasProperty("_EmissionColor") &&
                    material.GetColor("_EmissionColor").maxColorComponent <= 0f)
                {
                    material.SetColor("_EmissionColor", Color.white);
                }
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
        }

        /// <summary>統合マテリアルがAutodesk Interactiveシェーダーになるようにする（バリアントは維持）</summary>
        private static void EnsureAutodeskInteractiveShader(Material material, BakeReport report)
        {
            if (material.shader != null &&
                material.shader.name.StartsWith(MaterialModeProfiles.AutodeskInteractiveShaderName,
                    StringComparison.Ordinal))
            {
                return; // 既にAutodesk Interactive系（Transparent/Masked含む）
            }

            Shader ai = Shader.Find(MaterialModeProfiles.AutodeskInteractiveShaderName);
            if (ai != null)
            {
                material.shader = ai;
            }
            else
            {
                report.warnings.Add(
                    "Autodesk Interactiveシェーダーが見つからないため、ベースマテリアルのシェーダーのまま統合しました。");
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] segments = path.Split('/');
            string current = segments[0]; // 通常 "Assets"
            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }
                current = next;
            }
        }

        /// <summary>既存アセットがあればGUIDを維持したまま差し替える</summary>
        private static Mesh SaveMeshAsset(Mesh mesh, string assetPath)
        {
            // メインアセット名はファイル名と一致させる必要がある（CopySerializedで名前も上書きされるため）
            mesh.name = Path.GetFileNameWithoutExtension(assetPath);

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, assetPath);
            return mesh;
        }

        private static Material SaveMaterialAsset(Material material, string assetPath)
        {
            // メインアセット名はファイル名と一致させる必要がある（ベースマテリアルの名前が引き継がれるため）
            material.name = Path.GetFileNameWithoutExtension(assetPath);

            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(material, existing);
                existing.name = Path.GetFileNameWithoutExtension(assetPath);
                UnityEngine.Object.DestroyImmediate(material);
                return existing;
            }
            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        /// <param name="isNormal">ノーマルマップとしてインポートする</param>
        /// <param name="isLinear">リニアデータ（メタリック/AO等）としてsRGBを無効にする</param>
        private static Texture2D SaveTextureAsset(
            Texture2D texture, string assetPath, int maxSize, bool isNormal, bool isLinear)
        {
            string absolutePath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath));
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath);

            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !(isNormal || isLinear);
                importer.maxTextureSize = Mathf.Max(maxSize, 32);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        // ---------------------------------------------------------------
        // シーン出力
        // ---------------------------------------------------------------

        /// <summary>
        /// ベイク結果をプレハブとして保存し、アセンブリの子としてそのインスタンスを配置する。
        /// プレハブは既存があれば LoadPrefabContents で中身だけ更新するため、
        /// 再ベイクしてもプレハブ自体のGUIDと内部のコンポーネントのfileIDが維持され、
        /// 配布物としての参照整合性が保たれる。
        /// </summary>
        private static string UpdateScenePrefab(
            MeshBakeAssembly assembly, Mesh mesh, Material[] materials,
            string directory, string objectName)
        {
            string prefabPath = $"{directory}/{objectName}.prefab";

            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existingPrefab != null)
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    ConfigureBakedObject(contents, objectName, mesh, materials, assembly.markStatic);
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            else
            {
                var temp = new GameObject(objectName);
                try
                {
                    ConfigureBakedObject(temp, objectName, mesh, materials, assembly.markStatic);
                    PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(temp);
                }
            }

            // アセンブリの子としてプレハブインスタンスを配置する
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Transform existingChild = assembly.transform.Find(objectName);
            bool isLinkedInstance = existingChild != null &&
                PrefabUtility.GetCorrespondingObjectFromSource(existingChild.gameObject) == prefab;

            if (!isLinkedInstance)
            {
                if (existingChild != null)
                {
                    // 旧形式（非プレハブ）の生成物は置き換える
                    Undo.DestroyObjectImmediate(existingChild.gameObject);
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(assembly.transform, false);
                Undo.RegisterCreatedObjectUndo(instance, "Create Baked Mesh Prefab Instance");
            }

            return prefabPath;
        }

        private static void ConfigureBakedObject(
            GameObject target, string objectName, Mesh mesh, Material[] materials, bool markStatic)
        {
            target.name = objectName;
            MeshFilter filter = target.GetComponent<MeshFilter>();
            if (filter == null) filter = target.AddComponent<MeshFilter>();
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = target.AddComponent<MeshRenderer>();

            filter.sharedMesh = mesh;
            renderer.sharedMaterials = materials;
            target.isStatic = markStatic;
        }
    }
}
