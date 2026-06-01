using UnityEditor;
using UnityEngine;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

[InitializeOnLoad]
public static class SceneUtils
{
    private static bool isSnapDragging = false;
    private static Vector2 dragStartPos;
    
    static SceneUtils()
    {
        // エディタの更新イベントにハンドラを追加
        EditorApplication.update += OnEditorUpdate;
        // 全てのエディタウィンドウに対してキーイベントを監視する
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemGUI;
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static void OnEditorUpdate()
    {
        // エディタの更新処理
    }

    private static void OnHierarchyWindowItemGUI(int instanceID, Rect selectionRect)
    {
        CheckKeyPress();
    }

    private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
    {
        CheckKeyPress();
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        CheckKeyPress();
        HandleViewSnap(sceneView);
    }

    private static void HandleViewSnap(SceneView sceneView)
    {
        Event e = Event.current;
        if (e == null) return;

        // Alt + 中ボタンドラッグの検出
        if (e.alt && e.button == 2)
        {
            if (e.type == EventType.MouseDown)
            {
                isSnapDragging = true;
                dragStartPos = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isSnapDragging)
            {
                Vector2 delta = e.mousePosition - dragStartPos;
                float threshold = 50f; // ピクセル単位の閾値

                if (Mathf.Abs(delta.x) > threshold || Mathf.Abs(delta.y) > threshold)
                {
                    // ドラッグ方向を判定
                    Vector3 snapRotation = Vector3.zero;

                    if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                    {
                        // 横方向のドラッグ
                        if (delta.x > 0)
                            snapRotation = new Vector3(0, 45, 0); // 右: Y軸45度回転
                        else
                            snapRotation = new Vector3(0, -45, 0); // 左: Y軸-45度回転
                    }
                    else
                    {
                        // 縦方向のドラッグ
                        if (delta.y < 0)
                            snapRotation = new Vector3(45, 0, 0); // 上: X軸45度回転
                        else
                            snapRotation = new Vector3(-45, 0, 0); // 下: X軸-45度回転
                    }

                    // カメラの回転を適用
                    Quaternion currentRotation = sceneView.rotation;
                    Quaternion snapDelta = Quaternion.Euler(snapRotation);
                    sceneView.rotation = snapDelta * currentRotation;

                    // 開始位置をリセット
                    dragStartPos = e.mousePosition;
                }

                e.Use();
            }
            else if (e.type == EventType.MouseUp && isSnapDragging)
            {
                isSnapDragging = false;
                e.Use();
            }
        }
        else if (e.type == EventType.MouseUp && isSnapDragging)
        {
            // Alt キーが離された場合
            isSnapDragging = false;
        }
    }

    private static void CheckKeyPress()
    {
        Event e = Event.current;
        if (e == null) return;

        // スペースキーが押されたかどうかをチェック
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space && !e.control)
        {
            // 選択中のオブジェクトを取得
            GameObject[] selectedObjects = Selection.gameObjects;

            // 選択中のオブジェクトのアクティブ状態を切り替え
            foreach (GameObject obj in selectedObjects)
            {
                Undo.RecordObject(obj, "Toggle Active State");
                obj.SetActive(!obj.activeSelf);
            }

            // イベントを使用済みに設定
            e.Use();
        }
        
        // Shift+Zキーが押されたかどうかをチェック
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Z && e.shift)
        {
            // アクティブなシーンビューを取得
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                // 現在のレンダリングモードを次のモードに切り替え
                switch (sceneView.renderMode)
                {
                    case DrawCameraMode.Textured:
                        sceneView.renderMode = DrawCameraMode.Wireframe;
                        break;
                    case DrawCameraMode.Wireframe:
                        sceneView.renderMode = DrawCameraMode.TexturedWire;
                        break;
                    case DrawCameraMode.TexturedWire:
                    default:
                        sceneView.renderMode = DrawCameraMode.Textured;
                        break;
                }
                
                // シーンビューを再描画
                sceneView.Repaint();
            }
            
            // イベントを使用済みに設定
            e.Use();
        }

        // Ctrl+Spaceキーが押されたかどうかをチェック
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space && e.control)
        {
            // 選択中のオブジェクトを取得
            GameObject[] selectedObjects = Selection.gameObjects;

            // 選択中のオブジェクトの'Tag'を切り替え
            foreach (GameObject obj in selectedObjects)
            {
                Undo.RecordObject(obj, "Toggle EditorOnly Tag");
                if (obj.CompareTag("EditorOnly"))
                {
                    // 'EditorOnly'タグを外す
                    obj.tag = "Untagged";
                }
                else
                {
                    // 'EditorOnly'タグを付ける
                    obj.tag = "EditorOnly";
                }
            }

            // イベントを使用済みに設定
            e.Use();
        }
    }

    [MenuItem("Assets/Show in Explorer Here", false, 2010)]
    private static void ShowInExplorerHere()
    {
        string activeFolder = GetActiveProjectWindowFolder();
        if (string.IsNullOrEmpty(activeFolder))
        {
            activeFolder = "Assets";
        }

        string fullPath = ConvertUnityPathToFullPath(activeFolder);
        if (!string.IsNullOrEmpty(fullPath) && Directory.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + fullPath + "\"",
                UseShellExecute = true
            });
        }
    }

    [MenuItem("Assets/Show in Explorer Here", true)]
    private static bool ValidateShowInExplorerHere()
    {
        string activeFolder = GetActiveProjectWindowFolder();
        if (string.IsNullOrEmpty(activeFolder))
        {
            activeFolder = "Assets";
        }

        string fullPath = ConvertUnityPathToFullPath(activeFolder);
        return !string.IsNullOrEmpty(fullPath) && Directory.Exists(fullPath);
    }

    [MenuItem("Assets/Open Project in Code", false, 2020)]
    private static void OpenProjectInCode()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "code",
            Arguments = "\"" + projectRoot + "\"",
            UseShellExecute = true
        });
    }

    [MenuItem("Assets/Open Project in Code", true)]
    private static bool ValidateOpenProjectInCode()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return !string.IsNullOrEmpty(projectRoot) && Directory.Exists(projectRoot);
    }

    private static string GetActiveProjectWindowFolder()
    {
        Type projectWindowUtilType = typeof(ProjectWindowUtil);
        MethodInfo method = projectWindowUtilType.GetMethod(
            "GetActiveFolderPath",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        if (method == null)
        {
            return "Assets";
        }

        object result = method.Invoke(null, null);
        return result as string;
    }

    private static string ConvertUnityPathToFullPath(string unityPath)
    {
        if (string.IsNullOrEmpty(unityPath))
        {
            return null;
        }

        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        if (string.IsNullOrEmpty(projectRoot))
        {
            return null;
        }

        string normalized = unityPath.Replace('\\', '/');
        string fullPath = Path.GetFullPath(Path.Combine(projectRoot, normalized));
        return fullPath;
    }
}