using UnityEngine;
using UnityEditor;

namespace CanvasStudio
{
    public class EditorCallbacks
    {
        private CanvasStudio canvasStudio;
        
        public EditorCallbacks(CanvasStudio studio)
        {
            canvasStudio = studio;
        }
        
        public void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnEditorQuitting;
            
            SceneView.duringSceneGui += OnSceneGUI;
            
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }
        
        public void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
            
            SceneView.duringSceneGui -= OnSceneGUI;
            
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }
        
        void OnEditorUpdate()
        {
            var core = canvasStudio.core;
            
            // キーアップの検出
            if (Event.current != null && Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Space)
            {
                canvasStudio.uiDrawer.HandleKeyUp();
            }
            
            // プレビューが必要でない状態での自動復元
            if (core.needsMaterialRestore && !core.isPreviewActive)
            {
                canvasStudio.textureUtilities.RestoreAllMaterials();
            }
        }
        
        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                // プレイモード変更時にマテリアルを復元
                canvasStudio.textureUtilities.RestoreAllMaterials();
                canvasStudio.core.CleanupAllResources();
            }
        }
        
        void OnEditorQuitting()
        {
            // エディター終了時のクリーンアップ
            canvasStudio.textureUtilities.RestoreAllMaterials();
            canvasStudio.core.CleanupAllResources();
        }
        
        void OnSceneGUI(SceneView sceneView)
        {
            // シーンビューでのカスタム処理が必要な場合
        }
        
        void OnUndoRedoPerformed()
        {
            // Unity標準のUndo/Redoが実行された場合の処理
            canvasStudio.Repaint();
        }
        
        public void OnSelectionChange()
        {
            canvasStudio.uiDrawer.HandleSelectionChange();
        }
        
        public void OnDestroy()
        {
            var core = canvasStudio.core;
            
            try
            {
                if (canvasStudio.selectionSystem.IsSelectionPenMode)
                {
                    canvasStudio.selectionSystem.ExitSelectionPenMode(false);
                }
                
                canvasStudio.textureUtilities.RestoreAllMaterials();
                canvasStudio.undoSystem.ClearAll();
                canvasStudio.selectionSystem.CleanupResources();
                core.CleanupAllResources();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CanvasStudio: OnDestroy cleanup warning: {e.Message}");
            }
        }
        
        public void OnFocus()
        {
            canvasStudio.Repaint();
        }
        
        public void OnLostFocus()
        {
            var core = canvasStudio.core;
            
            // フォーカスを失った時に描画状態をリセット
            core.isPainting = false;
            core.isCurrentlyPainting = false;
            core.isDraggingUV = false;
            core.isSpoidMode = false;
            core.strokePaintedPixels.Clear();
            
            canvasStudio.Repaint();
        }
    }
}
