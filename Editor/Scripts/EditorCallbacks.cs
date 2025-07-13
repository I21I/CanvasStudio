using UnityEngine;
using UnityEditor;

namespace CanvasStudio
{
    public partial class CanvasStudio : EditorWindow
    {
        void OnEnable()
        {
            try
            {
                SceneView.duringSceneGui += OnSceneGUI;
                Selection.selectionChanged += OnSelectionChange;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                EditorApplication.update += OnEditorUpdate;
                EditorApplication.quitting += OnApplicationQuitting;
                
                if (brushCursorTexture == null || bucketCursorTexture == null)
                {
                    try
                    {
                        CreateBrushCursor();
                        CreateBucketCursor();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Canvas Studio: OnEnable時のカーソルテクスチャ作成エラー: {e.Message}");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Canvas Studio: OnEnable エラー: {e.Message}");
            }
        }
        
        void OnDisable()
        {
            try
            {
                // フラグをリセット
                ignoreSelectionChange = false;
                ignoreSelectionChangeUntil = 0f;
                lastShowButtonTime = 0f;

                if (!preventTextureRestore)
                {
                    RestoreAllMaterials();
                }
                
                SceneView.duringSceneGui -= OnSceneGUI;
                Selection.selectionChanged -= OnSelectionChange;
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.quitting -= OnApplicationQuitting;
                
                if (selectionPenMode)
                {
                    ExitSelectionPenMode(false);
                }
                
                CleanupAllResources();
            }
            catch (System.Exception)
            {
                Debug.LogError("Canvas Studio: OnDisable エラー");
            }
        }

        void OnDestroy()
        {
            try
            {
                if (!preventTextureRestore)
                {
                    RestoreAllMaterials();
                }
                
                if (selectionPenMode)
                {
                    ExitSelectionPenMode(false);
                }
                
                // 統一Undoスタックのクリーンアップ
                foreach (var undoRecord in undoStack)
                {
                    if (undoRecord.undoState != null)
                    {
                        undoRecord.undoState.Release();
                    }
                }
                undoStack.Clear();
                
                foreach (var redoRecord in redoStack)
                {
                    if (redoRecord.undoState != null)
                    {
                        redoRecord.undoState.Release();
                    }
                }
                redoStack.Clear();
                
                CleanupAllResources();
            }
            catch (System.Exception)
            {
                Debug.LogError("Canvas Studio: OnDestroy エラー");
                try 
                { 
                    if (!preventTextureRestore)
                    {
                        RestoreAllMaterials(); 
                    }
                } 
                catch { }
            }
        }

        void OnSelectionChange()
        {
            if (Selection.activeObject is ComputeShader) return;
            
            // 表示ボタン押下後の一定時間は自動選択を無視
            if (ignoreSelectionChange || Time.realtimeSinceStartup < ignoreSelectionChangeUntil)
            {
                return;
            }
            
            if (Selection.activeGameObject != null)
            {
                GameObject selected = Selection.activeGameObject;
                Renderer renderer = selected.GetComponent<Renderer>();
                if (renderer != null && selected != targetObject)
                {
                    SetTargetObject(selected);
                    Repaint();
                }
            }
            else if (Selection.activeObject is Texture2D texture && texture != directTexture)
            {
                SetDirectTexture(texture);
                Repaint();
            }
            else if (Selection.activeObject is Material material && material != targetMaterial)
            {
                SetTargetMaterial(material);
                Repaint();
            }
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                if (selectionPenMode)
                {
                    ExitSelectionPenMode(false);
                }
                if (!preventTextureRestore)
                {
                    RestoreAllMaterials();
                }
            }
        }
        
        void OnEditorUpdate()
        {
            if (this == null) return;
            
            try
            {
                if (Selection.activeObject is ComputeShader computeShader)
                {
                    if (computeShader.name.Contains("BrushPainter21") && targetObject != null)
                    {
                        Selection.activeObject = targetObject;
                    }
                }
            }
            catch
            {
            }
        }

        void OnApplicationQuitting()
        {
            try
            {
                if (!preventTextureRestore)
                {
                    RestoreAllMaterials();
                }
            }
            catch (System.Exception)
            {
                Debug.LogError("Canvas Studio: アプリケーション終了時復元エラー");
            }
        }
        
        void OnSceneGUI(SceneView sceneView)
        {
            if ((targetObject == null || targetRenderer == null) && !isDirectTextureMode) return;
            if (workingTexture == null) return;
            
            Event e = Event.current;
            
            if (e.type == EventType.KeyDown)
            {
                if (e.control && e.keyCode == KeyCode.Z)
                {
                    PerformUnifiedUndo();
                    e.Use();
                }
                else if (e.control && e.keyCode == KeyCode.Y)
                {
                    PerformUnifiedRedo();
                    e.Use();
                }
            }
        }
    }
}
