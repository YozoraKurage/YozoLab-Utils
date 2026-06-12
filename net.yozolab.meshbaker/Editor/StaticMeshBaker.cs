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
    }

    /// <summary>1つのSkinnedMeshRendererの1サブメッシュ分のベイク済みデータ</summary>
    internal class BakePart
    {
        public Material material;
        /// <summary>所属する出力グループ（rendererGroupsのインデックス）</summary>
        public int groupIndex;
        public Vector3[] positions;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Vector2[] uv;
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
            List<BakePart> parts = ExtractParts(assembly, report);
            if (parts.Count == 0)
            {
                throw new InvalidOperationException("ベイク対象が見つかりませんでした。Renderer GroupsにRenderer（SkinnedMeshRenderer/MeshRenderer）を設定してください。");
            }

            List<BakeMaterialGroup> materialGroups = GroupByMaterial(parts);
            report.sourceMaterialCount = materialGroups.Count;

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
                bool present = materialGroups.Any(g =>
                    g.material != null && g.material.HasProperty(prop) && g.material.GetTexture(prop) != null);
                if (present) effectiveList.Add(prop);
            }
            string[] effectiveProperties = effectiveList.ToArray();

            MaterialAtlasBuilder.Result atlasResult = null;
            if (assembly.mergeMaterials)
            {
                PrepareUVsForAtlas(assembly, materialGroups, mainProperty, report);

                if (assembly.packingMode == AtlasPackingMode.UVIslands)
                {
                    // UVアイランド単位の詰め直し（UV書き換えも内部で行われる）
                    atlasResult = UVIslandAtlasPacker.Pack(
                        assembly, materialGroups, effectiveProperties,
                        assembly.atlasSize, assembly.atlasPadding, report.warnings);
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
                        effectiveProperties, assembly.atlasSize, assembly.atlasPadding,
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
            for (int groupIndex = 0; groupIndex < assembly.rendererGroups.Count; groupIndex++)
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

                Mesh combined = BuildCombinedMesh(submeshes);
                if (assembly.generateLightmapUVs)
                {
                    Unwrapping.GenerateSecondaryUVSet(combined);
                }
                report.vertexCount += combined.vertexCount;
                report.submeshCount += submeshes.Count;

                string suffix = GetGroupSuffix(assembly, groupIndex);
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

            AssetDatabase.SaveAssets();
            return report;
        }

        /// <summary>成果物アセット名のグループサフィックス。単一の無名グループなら付けない（従来互換）</summary>
        private static string GetGroupSuffix(MeshBakeAssembly assembly, int groupIndex)
        {
            BakeRendererGroup group = assembly.rendererGroups[groupIndex];
            if (assembly.rendererGroups.Count == 1 && string.IsNullOrEmpty(group.name)) return "";
            return "_" + (string.IsNullOrEmpty(group.name) ? $"Group{groupIndex + 1}" : group.name);
        }

        // ---------------------------------------------------------------
        // ベイクと抽出
        // ---------------------------------------------------------------

        private static List<BakePart> ExtractParts(MeshBakeAssembly assembly, BakeReport report)
        {
            var parts = new List<BakePart>();
            Transform root = assembly.transform;

            for (int groupIndex = 0; groupIndex < assembly.rendererGroups.Count; groupIndex++)
            foreach (Renderer renderer in assembly.rendererGroups[groupIndex].renderers)
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
                        sourceUV, sourceColors, toRoot);
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
            Vector2[] uv, Color32[] colors, Matrix4x4 toRoot)
        {
            var map = new Dictionary<int, int>();
            var part = new BakePart { indices = new int[triangles.Length] };
            var outPositions = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outTangents = new List<Vector4>();
            var outUV = new List<Vector2>();
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
                    outColors?.Add(colors[sourceIndex]);
                }
                part.indices[i] = newIndex;
            }

            part.positions = outPositions.ToArray();
            part.normals = outNormals.ToArray();
            part.tangents = outTangents.ToArray();
            part.uv = outUV.ToArray();
            part.colors = outColors?.ToArray();
            return part;
        }

        private static List<BakeMaterialGroup> GroupByMaterial(List<BakePart> parts)
        {
            var groups = new List<BakeMaterialGroup>();
            var byMaterial = new Dictionary<Material, BakeMaterialGroup>();
            foreach (BakePart part in parts)
            {
                if (!byMaterial.TryGetValue(part.material, out BakeMaterialGroup group))
                {
                    group = new BakeMaterialGroup { material = part.material };
                    byMaterial.Add(part.material, group);
                    groups.Add(group);
                }
                group.parts.Add(part);
            }
            return groups;
        }

        // ---------------------------------------------------------------
        // UV処理
        // ---------------------------------------------------------------

        /// <summary>
        /// アトラス化に向けて各グループのUVを整える。
        /// Tiling/Offsetの焼き込み → 整数オフセットの除去 → 使用UV範囲の計算を行う。
        /// 使用範囲が[0,1]を超える場合はタイリングごと切り出される（解像度低下に注意）。
        /// </summary>
        private static void PrepareUVsForAtlas(
            MeshBakeAssembly assembly, List<BakeMaterialGroup> groups, string mainProperty, BakeReport report)
        {
            foreach (BakeMaterialGroup group in groups)
            {
                if (assembly.bakeTextureST && group.material.HasProperty(mainProperty))
                {
                    Vector2 scale = group.material.GetTextureScale(mainProperty);
                    Vector2 offset = group.material.GetTextureOffset(mainProperty);
                    if (scale != Vector2.one || offset != Vector2.zero)
                    {
                        foreach (BakePart part in group.parts)
                        {
                            for (int i = 0; i < part.uv.Length; i++)
                            {
                                part.uv[i] = Vector2.Scale(part.uv[i], scale) + offset;
                            }
                        }
                    }
                }

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

        /// <summary>サブメッシュごとのBakePartリストから1つのメッシュを構築する</summary>
        private static Mesh BuildCombinedMesh(List<List<BakePart>> submeshes)
        {
            int submeshCount = submeshes.Count;
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv = new List<Vector2>();
            var colors = new List<Color32>();
            bool anyColors = submeshes.Any(s => s.Any(p => p.colors != null));
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
                    if (anyColors)
                    {
                        if (part.colors != null) colors.AddRange(part.colors);
                        else for (int i = 0; i < part.positions.Length; i++) colors.Add(new Color32(255, 255, 255, 255));
                    }
                    foreach (int index in part.indices) indices.Add(index + offset);
                }
            }

            var mesh = new Mesh
            {
                indexFormat = positions.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv);
            if (anyColors) mesh.SetColors(colors);

            mesh.subMeshCount = submeshCount;
            for (int i = 0; i < submeshCount; i++)
            {
                mesh.SetTriangles(submeshIndices[i], i);
            }
            mesh.RecalculateBounds();
            return mesh;
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
                Texture2D savedTexture = SaveTextureAsset(
                    entry.Value, texturePath, assembly.atlasSize,
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
