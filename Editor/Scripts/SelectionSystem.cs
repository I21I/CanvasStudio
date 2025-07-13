using UnityEngine;
using UnityEditor;

namespace CanvasStudio
{
    public partial class CanvasStudio : EditorWindow
    {
        // 統一Undoシステム - 状態保存
        void SaveUnifiedUndoState(string operationName, UnifiedUndoType undoType = UnifiedUndoType.TextureOperation, bool saveTextureState = true)
        {
            if (isRestoringFromUndo) return;
            
            try
            {
                UnifiedUndoState undoState = new UnifiedUndoState();
                undoState.operationName = operationName;
                
                // 現在のパラメータを保存
                undoState.hue = colorHue;
                undoState.saturation = colorSaturation;
                undoState.brightness = colorBrightness;
                undoState.gamma = colorGamma;
                undoState.globalHue = globalColorHue;
                undoState.globalSaturation = globalColorSaturation;
                undoState.globalBrightness = globalColorBrightness;
                undoState.globalGamma = globalColorGamma;
                undoState.paintedAreaHue = paintedAreaColorHue;
                undoState.paintedAreaSaturation = paintedAreaColorSaturation;
                undoState.paintedAreaBrightness = paintedAreaColorBrightness;
                undoState.paintedAreaGamma = paintedAreaColorGamma;
                undoState.symmetryAxis = symmetryAxisPosition;
                undoState.brushStrength = brushStrength;
                undoState.paintOpacity = paintOpacity;
                undoState.wasSelectionMode = selectionPenMode;
                undoState.wasShowingSelection = isShowingSelectionPen;
                
                // ターゲット情報を保存
                undoState.savedTargetObject = targetObject;
                undoState.savedTargetMaterial = targetMaterial;
                undoState.savedDirectTexture = directTexture;
                undoState.savedOriginalTexture = originalTexture;
                undoState.savedIsDirectTextureMode = isDirectTextureMode;
                undoState.savedMaterialIndex = materialIndex;
                
                // テクスチャ状態をコピー（saveTextureStateがtrueの場合のみ）
                if (saveTextureState && workingTexture != null)
                {
                    undoState.workingTexture = CreateOptimizedRenderTexture(workingTexture.width, workingTexture.height);
                    Graphics.Blit(workingTexture, undoState.workingTexture);
                    
                    if (paintMask != null)
                    {
                        undoState.paintMask = CreateOptimizedRenderTexture(paintMask.width, paintMask.height);
                        Graphics.Blit(paintMask, undoState.paintMask);
                    }

                    if (paintColorTexture != null)
                    {
                        undoState.paintColorTexture = CreateOptimizedRenderTexture(paintColorTexture.width, paintColorTexture.height);
                        Graphics.Blit(paintColorTexture, undoState.paintColorTexture);
                    }
                    
                    if (selectionPenMode && selectionMask != null)
                    {
                        undoState.selectionMask = CreateOptimizedRenderTexture(selectionMask.width, selectionMask.height);
                        Graphics.Blit(selectionMask, undoState.selectionMask);
                    }
                    
                    if (originalTextureRT != null)
                    {
                        undoState.originalTextureRT = CreateOptimizedRenderTexture(originalTextureRT.width, originalTextureRT.height);
                        Graphics.Blit(originalTextureRT, undoState.originalTextureRT);
                    }
                }
                
                // Undoスタックに追加
                UnifiedUndoRecord record = new UnifiedUndoRecord
                {
                    operationType = undoType,
                    operationName = operationName,
                    undoState = undoState,
                    timestamp = System.DateTime.Now
                };
                
                undoStack.Add(record);
                
                // スタックサイズ制限
                if (undoStack.Count > maxUndoSteps)
                {
                    undoStack[0].undoState.Release();
                    undoStack.RemoveAt(0);
                }
                
                // Redoスタックをクリア
                ClearRedoStack();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Canvas Studio: Undo状態保存エラー: {e.Message}");
            }
        }
        
        void ClearRedoStack()
        {
            foreach (var redoRecord in redoStack)
            {
                redoRecord.undoState.Release();
            }
            redoStack.Clear();
        }
        
        // 統一Undo実行
        void PerformUnifiedUndo()
        {
            if (undoStack.Count == 0) return;
            
            try
            {
                isRestoringFromUndo = true;
                
                // 現在の状態をRedoスタックに保存
                SaveCurrentStateToRedoStack();
                
                // 最新の操作を取得
                UnifiedUndoRecord undoRecord = undoStack[undoStack.Count - 1];
                undoStack.RemoveAt(undoStack.Count - 1);
                
                // 状態を復元
                ApplyUnifiedUndoState(undoRecord.undoState);
                
                // リソースを解放
                undoRecord.undoState.Release();
                
                Repaint();
                SceneView.RepaintAll();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Canvas Studio: Undo実行エラー: {e.Message}");
            }
            finally
            {
                isRestoringFromUndo = false;
            }
        }
        
        // 統一Redo実行
        void PerformUnifiedRedo()
        {
            if (redoStack.Count == 0) return;
            
            try
            {
                isRestoringFromUndo = true;
                
                // 現在の状態をUndoスタックに保存
                SaveCurrentStateToUndoStack();
                
                // 最新のRedo操作を取得
                UnifiedUndoRecord redoRecord = redoStack[redoStack.Count - 1];
                redoStack.RemoveAt(redoStack.Count - 1);
                
                // 状態を復元
                ApplyUnifiedUndoState(redoRecord.undoState);
                
                // リソースを解放
                redoRecord.undoState.Release();
                
                Repaint();
                SceneView.RepaintAll();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Canvas Studio: Redo実行エラー: {e.Message}");
            }
            finally
            {
                isRestoringFromUndo = false;
            }
        }
        
        void SaveCurrentStateToRedoStack()
        {
            try
            {
                UnifiedUndoState currentState = new UnifiedUndoState();
                currentState.operationName = "Current State";
                
                // パラメータを保存
                currentState.hue = colorHue;
                currentState.saturation = colorSaturation;
                currentState.brightness = colorBrightness;
                currentState.gamma = colorGamma;
                currentState.globalHue = globalColorHue;
                currentState.globalSaturation = globalColorSaturation;
                currentState.globalBrightness = globalColorBrightness;
                currentState.globalGamma = globalColorGamma;
                currentState.paintedAreaHue = paintedAreaColorHue;
                currentState.paintedAreaSaturation = paintedAreaColorSaturation;
                currentState.paintedAreaBrightness = paintedAreaColorBrightness;
                currentState.paintedAreaGamma = paintedAreaColorGamma;
                currentState.symmetryAxis = symmetryAxisPosition;
                currentState.brushStrength = brushStrength;
                currentState.paintOpacity = paintOpacity;
                currentState.wasSelectionMode = selectionPenMode;
                currentState.wasShowingSelection = isShowingSelectionPen;
                
                // ターゲット情報を保存
                currentState.savedTargetObject = targetObject;
                currentState.savedTargetMaterial = targetMaterial;
                currentState.savedDirectTexture = directTexture;
                currentState.savedOriginalTexture = originalTexture;
                currentState.savedIsDirectTextureMode = isDirectTextureMode;
                currentState.savedMaterialIndex = materialIndex;
                
                // テクスチャ状態をコピー
                if (workingTexture != null)
                {
                    currentState.workingTexture = CreateOptimizedRenderTexture(workingTexture.width, workingTexture.height);
                    Graphics.Blit(workingTexture, currentState.workingTexture);
                }
                
                if (paintMask != null)
                {
                    currentState.paintMask = CreateOptimizedRenderTexture(paintMask.width, paintMask.height);
                    Graphics.Blit(paintMask, currentState.paintMask);
                }

                if (paintColorTexture != null)
                {
                    currentState.paintColorTexture = CreateOptimizedRenderTexture(paintColorTexture.width, paintColorTexture.height);
                    Graphics.Blit(paintColorTexture, currentState.paintColorTexture);
                }
                
                if (selectionPenMode && selectionMask != null)
                {
                    currentState.selectionMask = CreateOptimizedRenderTexture(selectionMask.width, selectionMask.height);
                    Graphics.Blit(selectionMask, currentState.selectionMask);
                }
                
                if (originalTextureRT != null)
                {
                    currentState.originalTextureRT = CreateOptimizedRenderTexture(originalTextureRT.width, originalTextureRT.height);
                    Graphics.Blit(originalTextureRT, currentState.originalTextureRT);
                }
                
                UnifiedUndoRecord record = new UnifiedUndoRecord
                {
                    operationType = UnifiedUndoType.TextureOperation,
                    operationName = "Current State",
                    undoState = currentState,
                    timestamp = System.DateTime.Now
                };
                
                redoStack.Add(record);
                
                if (redoStack.Count > maxUndoSteps)
                {
                    redoStack[0].undoState.Release();
                    redoStack.RemoveAt(0);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Canvas Studio: Redoスタック保存エラー: {e.Message}");
            }
        }

        void SaveCurrentStateToUndoStack()
        {
            try
            {
                UnifiedUndoState currentState = new UnifiedUndoState();
                currentState.operationName = "Before Redo";
                
                // パラメータを保存
                currentState.hue = colorHue;
                currentState.saturation = colorSaturation;
                currentState.brightness = colorBrightness;
                currentState.gamma = colorGamma;
                currentState.globalHue = globalColorHue;
                currentState.globalSaturation = globalColorSaturation;
                currentState.globalBrightness = globalColorBrightness;
                currentState.globalGamma = globalColorGamma;
                currentState.paintedAreaHue = paintedAreaColorHue;
                currentState.paintedAreaSaturation = paintedAreaColorSaturation;
                currentState.paintedAreaBrightness = paintedAreaColorBrightness;
                currentState.paintedAreaGamma = paintedAreaColorGamma;
                currentState.symmetryAxis = symmetryAxisPosition;
                currentState.brushStrength = brushStrength;
                currentState.paintOpacity = paintOpacity;
                currentState.wasSelectionMode = selectionPenMode;
                currentState.wasShowingSelection = isShowingSelectionPen;
                
                // ターゲット情報を保存
                currentState.savedTargetObject = targetObject;
                currentState.savedTargetMaterial = targetMaterial;
                currentState.savedDirectTexture = directTexture;
                currentState.savedOriginalTexture = originalTexture;
                currentState.savedIsDirectTextureMode = isDirectTextureMode;
                currentState.savedMaterialIndex = materialIndex;
                
                // テクスチャ状態をコピー
                if (workingTexture != null)
                {
                    currentState.workingTexture = CreateOptimizedRenderTexture(workingTexture.width, workingTexture.height);
                    Graphics.Blit(workingTexture, currentState.workingTexture);
                }
                
                if (paintMask != null)
                {
                    currentState.paintMask = CreateOptimizedRenderTexture(paintMask.width, paintMask.height);
                    Graphics.Blit(paintMask, currentState.paintMask);
                }

                if (paintColorTexture != null)
                {
                    currentState.paintColorTexture = CreateOptimizedRenderTexture(paintColorTexture.width, paintColorTexture.height);
                    Graphics.Blit(paintColorTexture, currentState.paintColorTexture);
                }
                
                if (selectionPenMode && selectionMask != null)
                {
                    currentState.selectionMask = CreateOptimizedRenderTexture(selectionMask.width, selectionMask.height);
                    Graphics.Blit(selectionMask, currentState.selectionMask);
                }
                
                if (originalTextureRT != null)
                {
                    currentState.originalTextureRT = CreateOptimizedRenderTexture(originalTextureRT.width, originalTextureRT.height);
                    Graphics.Blit(originalTextureRT, currentState.originalTextureRT);
                }
                
                UnifiedUndoRecord record = new UnifiedUndoRecord
                {
                    operationType = UnifiedUndoType.TextureOperation,
                    operationName = "Before Redo",
                    undoState = currentState,
                    timestamp = System.DateTime.Now
                };
                
                undoStack.Add(record);
                
                if (undoStack.Count > maxUndoSteps)
                {
                    undoStack[0].undoState.Release();
                    undoStack.RemoveAt(0);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Canvas Studio: Undoスタック保存エラー: {e.Message}");
            }
        }

        void ApplyUnifiedUndoState(UnifiedUndoState state)
        {
            if (state == null) return;
            
            try
            {
                // ターゲット情報を復元
                bool targetChanged = (targetObject != state.savedTargetObject) ||
                                   (targetMaterial != state.savedTargetMaterial) ||
                                   (directTexture != state.savedDirectTexture) ||
                                   (isDirectTextureMode != state.savedIsDirectTextureMode) ||
                                   (materialIndex != state.savedMaterialIndex);
                
                if (targetChanged)
                {
                    // 現在のプレビューを復元
                    if (!preventTextureRestore)
                    {
                        RestoreAllMaterials();
                    }
                    
                    // 選択ペンモードを一旦終了
                    if (selectionPenMode)
                    {
                        ExitSelectionPenMode(false);
                    }
                    
                    // ターゲットを復元
                    targetObject = state.savedTargetObject;
                    targetMaterial = state.savedTargetMaterial;
                    directTexture = state.savedDirectTexture;
                    originalTexture = state.savedOriginalTexture;
                    isDirectTextureMode = state.savedIsDirectTextureMode;
                    materialIndex = state.savedMaterialIndex;
                    
                    if (targetObject != null)
                    {
                        targetRenderer = targetObject.GetComponent<Renderer>();
                    }
                    else
                    {
                        targetRenderer = null;
                    }
                    
                    // テクスチャシステムを再構築
                    if (isDirectTextureMode && directTexture != null)
                    {
                        SetupRenderTextures();
                    }
                    else if (targetObject != null && targetRenderer != null)
                    {
                        if (targetRenderer.sharedMaterials != null && materialIndex < targetRenderer.sharedMaterials.Length)
                        {
                            targetMaterial = targetRenderer.sharedMaterials[materialIndex];
                            SetupRenderTextures();
                        }
                    }
                    else if (targetMaterial != null)
                    {
                        SetupRenderTextures();
                    }
                }
                
                // パラメータを復元
                colorHue = state.hue;
                colorSaturation = state.saturation;
                colorBrightness = state.brightness;
                colorGamma = state.gamma;
                globalColorHue = state.globalHue;
                globalColorSaturation = state.globalSaturation;
                globalColorBrightness = state.globalBrightness;
                globalColorGamma = state.globalGamma;
                paintedAreaColorHue = state.paintedAreaHue;
                paintedAreaColorSaturation = state.paintedAreaSaturation;
                paintedAreaColorBrightness = state.paintedAreaBrightness;
                paintedAreaColorGamma = state.paintedAreaGamma;
                symmetryAxisPosition = state.symmetryAxis;
                brushStrength = state.brushStrength;
                paintOpacity = state.paintOpacity;
                
                // テクスチャ状態を復元（保存されている場合のみ）
                if (state.workingTexture != null && workingTexture != null)
                {
                    Graphics.Blit(state.workingTexture, workingTexture);
                }
                
                if (state.paintMask != null && paintMask != null)
                {
                    Graphics.Blit(state.paintMask, paintMask);
                }

                if (state.paintColorTexture != null && paintColorTexture != null)
                {
                    Graphics.Blit(state.paintColorTexture, paintColorTexture);
                }
                
                if (state.selectionMask != null && selectionMask != null)
                {
                    Graphics.Blit(state.selectionMask, selectionMask);
                }
                
                if (state.originalTextureRT != null && originalTextureRT != null)
                {
                    Graphics.Blit(state.originalTextureRT, originalTextureRT);
                }
                
                // モードの復元
                bool wasInSelectionMode = selectionPenMode;
                if (state.wasSelectionMode != wasInSelectionMode)
                {
                    if (state.wasSelectionMode)
                    {
                        EnterSelectionPenMode();
                    }
                    else
                    {
                        ExitSelectionPenMode(false);
                    }
                }
                
                isShowingSelectionPen = state.wasShowingSelection;
                
                // 表示の更新
                UpdateDisplayAfterUndo();
                
                meshTextureDirty = true;
                CreateBrushCursor();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Canvas Studio: Undo状態適用エラー: {e.Message}");
            }
        }
        
        void UpdateDisplayAfterUndo()
        {
            try
            {
                if (selectionPenMode)
                {
                    UpdateSelectionDisplay();
                    UpdateSelectionAreaStatus();
                }
                else
                {
                    if (!isDirectTextureMode && targetMaterial != null)
                    {
                        SetMaterialPreview(targetMaterial, workingTexture);
                    }
                }
                
                // 色調補正の更新フラグを設定
                needsColorUpdate = true;
                needsGlobalUpdate = true;
                needsPaintedAreaUpdate = true;
                
                Repaint();
                SceneView.RepaintAll();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Canvas Studio: 表示更新エラー: {e.Message}");
            }
        }
    }
}
