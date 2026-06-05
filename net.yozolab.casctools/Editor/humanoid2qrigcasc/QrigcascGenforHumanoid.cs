using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Unity Humanoidアバターからqrigcascファイルを生成するエディタースクリプト
/// </summary>
public class QrigcascGenforHumanoid : EditorWindow
{
    private Object fbxAsset;
    private Avatar targetAvatar;
    private GameObject targetGameObject;
    private string outputPath = "Assets/GeneratedTemplate.qrigcasc";
    private bool alignPelvis = false;
    private bool createLayers = false;
    private bool includeSettings = false;

    [MenuItem("YozoLab/Qrigcasc Generator")]
    public static void ShowWindow()
    {
        GetWindow<QrigcascGenforHumanoid>(".qrigcasc Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Unity Humanoid to qrigcasc", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        fbxAsset = EditorGUILayout.ObjectField("FBX File (Optional)", fbxAsset, typeof(Object), false);
        if (EditorGUI.EndChangeCheck() && fbxAsset != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(fbxAsset);
            if (assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer != null)
                {
                    targetAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(assetPath);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (prefab != null)
                    {
                        targetGameObject = prefab;
                    }
                    
                    // FBX名からoutputPathを生成
                    string fbxName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                    string directory = System.IO.Path.GetDirectoryName(outputPath);
                    outputPath = System.IO.Path.Combine(directory, fbxName + ".qrigcasc").Replace("\\", "/");
                }
            }
        }
        
        EditorGUI.BeginDisabledGroup(fbxAsset != null);
        targetAvatar = EditorGUILayout.ObjectField("Target Avatar", targetAvatar, typeof(Avatar), false) as Avatar;
        targetGameObject = EditorGUILayout.ObjectField("Target GameObject", targetGameObject, typeof(GameObject), true) as GameObject;
        EditorGUI.EndDisabledGroup();
        
        outputPath = EditorGUILayout.TextField("Output Path", outputPath);
        
        GUILayout.Space(10);
        GUILayout.Label("Settings", EditorStyles.boldLabel);
        includeSettings = EditorGUILayout.Toggle("Include Settings in JSON", includeSettings);
        
        EditorGUI.BeginDisabledGroup(!includeSettings);
        alignPelvis = EditorGUILayout.Toggle("Is align pelvis", alignPelvis);
        createLayers = EditorGUILayout.Toggle("Is create layers", createLayers);
        EditorGUI.EndDisabledGroup();
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Generate qrigcasc"))
        {
            GenerateQrigcasc();
        }
    }

    private void GenerateQrigcasc()
    {
        if (targetAvatar == null || targetGameObject == null)
        {
            EditorUtility.DisplayDialog("Error", "Please set both Target Avatar and Target GameObject", "OK");
            return;
        }

        var qrigcasc = new QrigcascData();
        var humanDescription = targetAvatar.humanDescription;
        
        // Transform階層を構築
        Dictionary<string, Transform> boneMap = new Dictionary<string, Transform>();
        BuildBoneMap(targetGameObject.transform, boneMap);
        
        // HumanBodyBonesとボーン名のマッピングを取得
        Dictionary<HumanBodyBones, string> humanBoneNames = new Dictionary<HumanBodyBones, string>();
        foreach (var humanBone in humanDescription.human)
        {
            // humanNameにはスペースが含まれる場合があるので削除してからパース
            string humanNameNoSpace = humanBone.humanName.Replace(" ", "");
            if (System.Enum.TryParse<HumanBodyBones>(humanNameNoSpace, out HumanBodyBones boneType))
            {
                humanBoneNames[boneType] = humanBone.boneName;
            }
            else
            {
                Debug.LogWarning($"Could not parse bone: '{humanBone.humanName}' (tried '{humanNameNoSpace}')");
            }
        }

        // Bodyセクションを生成
        qrigcasc.Document.Add(CreateBodyDocument(humanBoneNames, boneMap));
        
        // Left handセクションを生成
        var leftHandDoc = CreateLeftHandDocument(humanBoneNames, boneMap);
        if (leftHandDoc.Sections.Count > 0)
        {
            qrigcasc.Document.Add(leftHandDoc);
        }
        else
        {
            Debug.LogWarning("Left hand finger bones not found in Humanoid avatar. Skipping left hand section.");
        }
        
        // Right handセクションを生成
        var rightHandDoc = CreateRightHandDocument(humanBoneNames, boneMap);
        if (rightHandDoc.Sections.Count > 0)
        {
            qrigcasc.Document.Add(rightHandDoc);
        }
        else
        {
            Debug.LogWarning("Right hand finger bones not found in Humanoid avatar. Skipping right hand section.");
        }

        // Settingsを設定
        if (includeSettings)
        {
            qrigcasc.Settings = new QrigcascSettings
            {
                IsAlignPelvis = alignPelvis,
                IsCreateLayers = createLayers
            };
        }

        // JSONとして出力
        string json = JsonUtility.ToJson(qrigcasc, true);
        
        // JsonUtilityは配列のフォーマットが微妙なので、手動で整形
        json = FormatJson(json, qrigcasc, includeSettings);
        
        // BOMなしUTF-8で保存
        var utf8WithoutBom = new System.Text.UTF8Encoding(false);
        File.WriteAllText(outputPath, json, utf8WithoutBom);
        AssetDatabase.Refresh();
        
        EditorUtility.DisplayDialog("Success", $"qrigcasc file generated at:\n{outputPath}", "OK");
    }

    private void BuildBoneMap(Transform root, Dictionary<string, Transform> boneMap)
    {
        boneMap[root.name] = root;
        foreach (Transform child in root)
        {
            BuildBoneMap(child, boneMap);
        }
    }

    private string[] GetJointPath(string boneName, Dictionary<string, Transform> boneMap, Dictionary<HumanBodyBones, string> humanBoneNames)
    {
        if (!boneMap.ContainsKey(boneName))
            return new string[0];
        
        // Humanoidボーン名のセットを作成（高速検索用）
        HashSet<string> humanoidBoneNames = new HashSet<string>(humanBoneNames.Values);
        
        List<string> path = new List<string>();
        Transform current = boneMap[boneName].parent;
        
        // 親を辿っていき、Humanoidボーンとして登録されているものだけをパスに追加
        while (current != null)
        {
            if (humanoidBoneNames.Contains(current.name))
            {
                path.Insert(0, current.name);
            }
            current = current.parent;
        }
        
        return path.ToArray();
    }

    private DocumentGroup CreateBodyDocument(Dictionary<HumanBodyBones, string> humanBoneNames, Dictionary<string, Transform> boneMap)
    {
        var doc = new DocumentGroup { Title = "Body", Sections = new List<Section>() };
        
        // Bodyセクション
        var bodySection = new Section { SectionName = "Body", Names = new List<BoneMapping>() };
        AddBoneMapping(bodySection, "pelvis", HumanBodyBones.Hips, humanBoneNames, boneMap);
        AddBoneMapping(bodySection, "stomach", HumanBodyBones.Spine, humanBoneNames, boneMap);
        AddBoneMapping(bodySection, "chest", HumanBodyBones.Chest, humanBoneNames, boneMap);
        AddBoneMapping(bodySection, "neck", HumanBodyBones.Neck, humanBoneNames, boneMap);
        AddBoneMapping(bodySection, "head", HumanBodyBones.Head, humanBoneNames, boneMap);
        doc.Sections.Add(bodySection);
        
        // Left armセクション
        var leftArmSection = new Section { SectionName = "Left arm", Names = new List<BoneMapping>() };
        AddBoneMapping(leftArmSection, "clavicle_l", HumanBodyBones.LeftShoulder, humanBoneNames, boneMap);
        AddBoneMapping(leftArmSection, "arm_l", HumanBodyBones.LeftUpperArm, humanBoneNames, boneMap);
        AddBoneMapping(leftArmSection, "forearm_l", HumanBodyBones.LeftLowerArm, humanBoneNames, boneMap);
        AddBoneMapping(leftArmSection, "hand_l", HumanBodyBones.LeftHand, humanBoneNames, boneMap);
        doc.Sections.Add(leftArmSection);
        
        // Right armセクション
        var rightArmSection = new Section { SectionName = "Right arm", Names = new List<BoneMapping>() };
        AddBoneMapping(rightArmSection, "clavicle_r", HumanBodyBones.RightShoulder, humanBoneNames, boneMap);
        AddBoneMapping(rightArmSection, "arm_r", HumanBodyBones.RightUpperArm, humanBoneNames, boneMap);
        AddBoneMapping(rightArmSection, "forearm_r", HumanBodyBones.RightLowerArm, humanBoneNames, boneMap);
        AddBoneMapping(rightArmSection, "hand_r", HumanBodyBones.RightHand, humanBoneNames, boneMap);
        doc.Sections.Add(rightArmSection);
        
        // Left legセクション
        var leftLegSection = new Section { SectionName = "Left leg", Names = new List<BoneMapping>() };
        AddBoneMapping(leftLegSection, "thigh_l", HumanBodyBones.LeftUpperLeg, humanBoneNames, boneMap);
        AddBoneMapping(leftLegSection, "calf_l", HumanBodyBones.LeftLowerLeg, humanBoneNames, boneMap);
        AddBoneMapping(leftLegSection, "foot_l", HumanBodyBones.LeftFoot, humanBoneNames, boneMap);
        AddBoneMapping(leftLegSection, "toe_l", HumanBodyBones.LeftToes, humanBoneNames, boneMap);
        doc.Sections.Add(leftLegSection);
        
        // Right legセクション
        var rightLegSection = new Section { SectionName = "Right leg", Names = new List<BoneMapping>() };
        AddBoneMapping(rightLegSection, "thigh_r", HumanBodyBones.RightUpperLeg, humanBoneNames, boneMap);
        AddBoneMapping(rightLegSection, "calf_r", HumanBodyBones.RightLowerLeg, humanBoneNames, boneMap);
        AddBoneMapping(rightLegSection, "foot_r", HumanBodyBones.RightFoot, humanBoneNames, boneMap);
        AddBoneMapping(rightLegSection, "toe_r", HumanBodyBones.RightToes, humanBoneNames, boneMap);
        doc.Sections.Add(rightLegSection);
        
        return doc;
    }

    private DocumentGroup CreateLeftHandDocument(Dictionary<HumanBodyBones, string> humanBoneNames, Dictionary<string, Transform> boneMap)
    {
        var doc = new DocumentGroup { Title = "Left hand", Sections = new List<Section>() };
        
        // Thumb
        var thumbSection = new Section { SectionName = "Thumb", Names = new List<BoneMapping>() };
        AddBoneMapping(thumbSection, "thumb_l_1", HumanBodyBones.LeftThumbProximal, humanBoneNames, boneMap);
        AddBoneMapping(thumbSection, "thumb_l_2", HumanBodyBones.LeftThumbIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(thumbSection, "thumb_l_3", HumanBodyBones.LeftThumbDistal, humanBoneNames, boneMap);
        if (thumbSection.Names.Count > 0)
            doc.Sections.Add(thumbSection);
        
        // Index finger
        var indexSection = new Section { SectionName = "Index finger", Names = new List<BoneMapping>() };
        AddBoneMapping(indexSection, "index_finger_l_1", HumanBodyBones.LeftIndexProximal, humanBoneNames, boneMap);
        AddBoneMapping(indexSection, "index_finger_l_2", HumanBodyBones.LeftIndexIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(indexSection, "index_finger_l_3", HumanBodyBones.LeftIndexDistal, humanBoneNames, boneMap);
        if (indexSection.Names.Count > 0)
            doc.Sections.Add(indexSection);
        
        // Middle finger
        var middleSection = new Section { SectionName = "Middle finger", Names = new List<BoneMapping>() };
        AddBoneMapping(middleSection, "middle_finger_l_1", HumanBodyBones.LeftMiddleProximal, humanBoneNames, boneMap);
        AddBoneMapping(middleSection, "middle_finger_l_2", HumanBodyBones.LeftMiddleIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(middleSection, "middle_finger_l_3", HumanBodyBones.LeftMiddleDistal, humanBoneNames, boneMap);
        if (middleSection.Names.Count > 0)
            doc.Sections.Add(middleSection);
        
        // Ring finger
        var ringSection = new Section { SectionName = "Ring finger", Names = new List<BoneMapping>() };
        AddBoneMapping(ringSection, "ring_finger_l_1", HumanBodyBones.LeftRingProximal, humanBoneNames, boneMap);
        AddBoneMapping(ringSection, "ring_finger_l_2", HumanBodyBones.LeftRingIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(ringSection, "ring_finger_l_3", HumanBodyBones.LeftRingDistal, humanBoneNames, boneMap);
        if (ringSection.Names.Count > 0)
            doc.Sections.Add(ringSection);
        
        // Pinky
        var pinkySection = new Section { SectionName = "Pinky", Names = new List<BoneMapping>() };
        AddBoneMapping(pinkySection, "pinky_l_1", HumanBodyBones.LeftLittleProximal, humanBoneNames, boneMap);
        AddBoneMapping(pinkySection, "pinky_l_2", HumanBodyBones.LeftLittleIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(pinkySection, "pinky_l_3", HumanBodyBones.LeftLittleDistal, humanBoneNames, boneMap);
        if (pinkySection.Names.Count > 0)
            doc.Sections.Add(pinkySection);
        
        return doc;
    }

    private DocumentGroup CreateRightHandDocument(Dictionary<HumanBodyBones, string> humanBoneNames, Dictionary<string, Transform> boneMap)
    {
        var doc = new DocumentGroup { Title = "Right hand", Sections = new List<Section>() };
        
        // Thumb
        var thumbSection = new Section { SectionName = "Thumb", Names = new List<BoneMapping>() };
        AddBoneMapping(thumbSection, "thumb_r_1", HumanBodyBones.RightThumbProximal, humanBoneNames, boneMap);
        AddBoneMapping(thumbSection, "thumb_r_2", HumanBodyBones.RightThumbIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(thumbSection, "thumb_r_3", HumanBodyBones.RightThumbDistal, humanBoneNames, boneMap);
        if (thumbSection.Names.Count > 0)
            doc.Sections.Add(thumbSection);
        
        // Index finger
        var indexSection = new Section { SectionName = "Index finger", Names = new List<BoneMapping>() };
        AddBoneMapping(indexSection, "index_finger_r_1", HumanBodyBones.RightIndexProximal, humanBoneNames, boneMap);
        AddBoneMapping(indexSection, "index_finger_r_2", HumanBodyBones.RightIndexIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(indexSection, "index_finger_r_3", HumanBodyBones.RightIndexDistal, humanBoneNames, boneMap);
        if (indexSection.Names.Count > 0)
            doc.Sections.Add(indexSection);
        
        // Middle finger
        var middleSection = new Section { SectionName = "Middle finger", Names = new List<BoneMapping>() };
        AddBoneMapping(middleSection, "middle_finger_r_1", HumanBodyBones.RightMiddleProximal, humanBoneNames, boneMap);
        AddBoneMapping(middleSection, "middle_finger_r_2", HumanBodyBones.RightMiddleIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(middleSection, "middle_finger_r_3", HumanBodyBones.RightMiddleDistal, humanBoneNames, boneMap);
        if (middleSection.Names.Count > 0)
            doc.Sections.Add(middleSection);
        
        // Ring finger
        var ringSection = new Section { SectionName = "Ring finger", Names = new List<BoneMapping>() };
        AddBoneMapping(ringSection, "ring_finger_r_1", HumanBodyBones.RightRingProximal, humanBoneNames, boneMap);
        AddBoneMapping(ringSection, "ring_finger_r_2", HumanBodyBones.RightRingIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(ringSection, "ring_finger_r_3", HumanBodyBones.RightRingDistal, humanBoneNames, boneMap);
        if (ringSection.Names.Count > 0)
            doc.Sections.Add(ringSection);
        
        // Pinky
        var pinkySection = new Section { SectionName = "Pinky", Names = new List<BoneMapping>() };
        AddBoneMapping(pinkySection, "pinky_r_1", HumanBodyBones.RightLittleProximal, humanBoneNames, boneMap);
        AddBoneMapping(pinkySection, "pinky_r_2", HumanBodyBones.RightLittleIntermediate, humanBoneNames, boneMap);
        AddBoneMapping(pinkySection, "pinky_r_3", HumanBodyBones.RightLittleDistal, humanBoneNames, boneMap);
        if (pinkySection.Names.Count > 0)
            doc.Sections.Add(pinkySection);
        
        return doc;
    }

    private void AddBoneMapping(Section section, string qrigBoneName, HumanBodyBones humanBone, 
        Dictionary<HumanBodyBones, string> humanBoneNames, Dictionary<string, Transform> boneMap)
    {
        if (!humanBoneNames.ContainsKey(humanBone))
            return;
        
        string boneName = humanBoneNames[humanBone];
        var mapping = new BoneMapping
        {
            BoneName = qrigBoneName,
            JointName = boneName,
            JointPath = GetJointPath(boneName, boneMap, humanBoneNames)
        };
        
        section.Names.Add(mapping);
    }

    private string FormatJson(string json, QrigcascData data, bool includeSettings)
    {
        // JsonUtilityの制限により、手動でJSONを構築
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("\t\"Document\": [");
        
        for (int i = 0; i < data.Document.Count; i++)
        {
            var doc = data.Document[i];
            sb.AppendLine("\t\t{");
            sb.AppendLine($"\t\t\t\"Title\": \"{doc.Title}\",");
            sb.AppendLine("\t\t\t\"Sections\": [");
            
            for (int j = 0; j < doc.Sections.Count; j++)
            {
                var section = doc.Sections[j];
                sb.AppendLine("\t\t\t\t{");
                sb.AppendLine($"\t\t\t\t\t\"Section\": \"{section.SectionName}\",");
                sb.AppendLine("\t\t\t\t\t\"Names\": [");
                
                for (int k = 0; k < section.Names.Count; k++)
                {
                    var name = section.Names[k];
                    sb.AppendLine("\t\t\t\t\t\t{");
                    sb.AppendLine($"\t\t\t\t\t\t\t\"Bone name\": \"{name.BoneName}\",");
                    sb.AppendLine($"\t\t\t\t\t\t\t\"Joint name\": \"{name.JointName}\",");
                    sb.Append("\t\t\t\t\t\t\t\"Joint path\": [");
                    
                    if (name.JointPath.Length > 0)
                    {
                        sb.AppendLine();
                        for (int l = 0; l < name.JointPath.Length; l++)
                        {
                            sb.Append($"\t\t\t\t\t\t\t\t\"{name.JointPath[l]}\"");
                            if (l < name.JointPath.Length - 1)
                                sb.AppendLine(",");
                            else
                                sb.AppendLine();
                        }
                        sb.AppendLine("\t\t\t\t\t\t\t]");
                    }
                    else
                    {
                        sb.AppendLine("]");
                    }
                    
                    sb.Append("\t\t\t\t\t\t}");
                    if (k < section.Names.Count - 1)
                        sb.AppendLine(",");
                    else
                        sb.AppendLine();
                }
                
                sb.Append("\t\t\t\t\t]");
                sb.AppendLine();
                sb.Append("\t\t\t\t}");
                if (j < doc.Sections.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
            
            sb.Append("\t\t\t]");
            sb.AppendLine();
            sb.Append("\t\t}");
            if (i < data.Document.Count - 1)
                sb.AppendLine(",");
            else
                sb.AppendLine();
        }
        
        sb.AppendLine("\t]");
        
        if (includeSettings && data.Settings != null)
        {
            sb.AppendLine("\t,");
            sb.AppendLine("\t\"Settings\": {");
            sb.AppendLine($"\t\t\"Is align pelvis\": {data.Settings.IsAlignPelvis.ToString().ToLower()},");
            sb.AppendLine($"\t\t\"Is create layers\": {data.Settings.IsCreateLayers.ToString().ToLower()}");
            sb.AppendLine("\t}");
        }
        else
        {
            sb.AppendLine();
        }
        
        sb.Append("}");
        
        return sb.ToString();
    }
}

// データ構造クラス
[System.Serializable]
public class QrigcascData
{
    public List<DocumentGroup> Document = new List<DocumentGroup>();
    public QrigcascSettings Settings;
}

[System.Serializable]
public class DocumentGroup
{
    public string Title;
    public List<Section> Sections;
}

[System.Serializable]
public class Section
{
    public string SectionName;
    public List<BoneMapping> Names;
}

[System.Serializable]
public class BoneMapping
{
    public string BoneName;
    public string JointName;
    public string[] JointPath;
}

[System.Serializable]
public class QrigcascSettings
{
    public bool IsAlignPelvis;
    public bool IsCreateLayers;
}
