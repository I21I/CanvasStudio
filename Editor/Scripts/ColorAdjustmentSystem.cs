using UnityEngine;
using UnityEditor;
using System.IO;

namespace CanvasStudio
{
    public class ColorAdjustmentSystem
    {
        private CanvasStudio canvasStudio;
        
        // 最適化用フラグ
        private bool needsColorUpdate = false;
        private bool needsGlobalUpdate = false;
        private bool needsPaintedAreaUpdate = false;
        private bool isProcessingGlobalColorAdjustment = false;
        private bool needsPaintOpacityUpdate = false;
        
        // ドラッグ状態管理
        private bool isDraggingGlobalHue = false;
        private bool isDraggingGlobalSaturation = false;
        private bool isDraggingGlobalBrightness = false;
        private bool isDraggingGlobalGamma = false;
        private bool isDraggingSelectionHue = false;
        private bool isDraggingSelectionSaturation = false;
        private bool isDraggingSelectionBrightness = false;
        private bool isDraggingSelectionGamma = false;
        private bool isDraggingPaintedAreaHue = false;
        private bool isDraggingPaintedAreaSaturation = false;
        private bool isDraggingPaintedAreaBrightness = false;
        private bool isDraggingPaintedAreaGamma = false;
        
        public ColorAdjustmentSystem(CanvasStudio studio)
        {
            canvasStudio = studio;
        }
        
        public bool NeedsColorUpdate
        {
            get => needsColorUpdate;
            set => needsColorUpdate = value;
        }
        
        public bool NeedsGlobalUpdate
        {
            get => needsGlobalUpdate;
            set => needsGlobalUpdate = value;
        }
        
        public bool NeedsPaintedAreaUpdate
        {
            get => needsPaintedAreaUpdate;
            set => needsPaintedAreaUpdate = value;
        }
        
        public bool NeedsPaintOpacityUpdate
        {
            get => needsPaintOpacityUpdate;
            set => needsPaintOpacityUpdate = value;
        }
        
        public void ProcessRealTimeUpdates()
        {
            var core = canvasStudio.core;
            if (core.workingTexture == null) return;
            
            // 参照テクスチャ色調補正のリアルタイム更新
            if (needsGlobalUpdate)
            {
                needsGlobalUpdate = false;
                ApplyGlobalColorAdjustmentRealtime();
            }
            
            // 描画部分色調補正のリアルタイム更新
            if (needsPaintedAreaUpdate)
            {
                needsPaintedAreaUpdate = false;
                ApplyPaintedAreaColorAdjustmentRealtime();
            }
            
            // 描画不透明度のリアルタイム更新
            if (needsPaintOpacityUpdate)
            {
                needsPaintOpacityUpdate = false;
                ApplyPaintOpacity();
            }
            
            // 選択色調補正のリアルタイム更新
            if (needsColorUpdate)
            {
                needsColorUpdate = false;
                ApplySelectionColorAdjustmentRealtime();
            }
        }
        
        public void HandleColorSliderWithUndo(string label, ref float value, float min, float max, ref bool isDragging, string undoName, System.Action onChanged)
        {
            Event currentEvent = Event.current;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(50));
            
            EditorGUI.BeginChangeCheck();
            float newValue = GUILayout.HorizontalSlider(value, min, max);
            bool changed = EditorGUI.EndChangeCheck();
            
            string currentText = FormatFloatValue(value);
            string newText = EditorGUILayout.TextField(currentText, GUILayout.Width(70));
            if (float.TryParse(newText, out float parsedValue))
            {
                parsedValue = Mathf.Clamp(parsedValue, min, max);
                if (Mathf.Abs(parsedValue - value) > 0.001f)
                {
                    newValue = parsedValue;
                    changed = true;
                }
            }
            
            float resetValue = (label == "色相") ? 0f : ((label == "彩度" || label == "明度") ? 1f : 0f);
            
            if (GUILayout.Button("↻", GUILayout.Width(25)))
            {
                canvasStudio.undoSystem.SaveUnifiedUndoState(undoName + " リセット", UnifiedUndoType.ParameterOperation, false);
                value = resetValue;
                isDragging = false;
                onChanged?.Invoke();
                canvasStudio.Repaint();
                EditorGUILayout.EndHorizontal();
                return;
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (changed)
            {
                if (!isDragging)
                {
                    canvasStudio.undoSystem.SaveUnifiedUndoState(undoName, UnifiedUndoType.ParameterOperation, false);
                    isDragging = true;
                }
                value = newValue;
                onChanged?.Invoke();
                canvasStudio.Repaint();
            }
            
            if (isDragging && (currentEvent.type == EventType.MouseUp || 
                (currentEvent.type == EventType.Repaint && EditorGUIUtility.hotControl == 0)))
            {
                isDragging = false;
            }
        }
        
        public void HandleGammaSliderWithUndo(string label, ref float value, ref bool isDragging, string undoName, System.Action onChanged)
        {
            Event currentEvent = Event.current;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(50));
            
            float minGamma = 0.01f;
            float maxGamma = 2f;
            
            float sliderPosition;
            if (value <= 1f)
            {
                sliderPosition = (value - minGamma) / (1f - minGamma) * 0.5f;
            }
            else
            {
                sliderPosition = 0.5f + (value - 1f) / (maxGamma - 1f) * 0.5f;
            }
            
            EditorGUI.BeginChangeCheck();
            float newSliderPosition = GUILayout.HorizontalSlider(sliderPosition, 0f, 1f);
            bool changed = EditorGUI.EndChangeCheck();
            
            float newGamma;
            if (newSliderPosition <= 0.5f)
            {
                newGamma = minGamma + (newSliderPosition / 0.5f) * (1f - minGamma);
            }
            else
            {
                newGamma = 1f + ((newSliderPosition - 0.5f) / 0.5f) * (maxGamma - 1f);
            }
            
            string currentText = FormatFloatValue(value);
            string newText = EditorGUILayout.TextField(currentText, GUILayout.Width(70));
            
            if (float.TryParse(newText, out float parsedGamma))
            {
                parsedGamma = Mathf.Clamp(parsedGamma, minGamma, maxGamma);
                if (Mathf.Abs(parsedGamma - value) > 0.001f)
                {
                    newGamma = parsedGamma;
                    changed = true;
                }
            }
            
            if (GUILayout.Button("↻", GUILayout.Width(25)))
            {
                canvasStudio.undoSystem.SaveUnifiedUndoState(undoName + " リセット", UnifiedUndoType.ParameterOperation, false);
                value = 1f;
                isDragging = false;
                onChanged?.Invoke();
                canvasStudio.Repaint();
                EditorGUILayout.EndHorizontal();
                return;
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (changed)
            {
                if (!isDragging)
                {
                    canvasStudio.undoSystem.SaveUnifiedUndoState(undoName, UnifiedUndoType.ParameterOperation, false);
                    isDragging = true;
                }
                value = newGamma;
                onChanged?.Invoke();
                canvasStudio.Repaint();
            }
            
            if (isDragging && (currentEvent.type == EventType.MouseUp || 
                (currentEvent.type == EventType.Repaint && EditorGUIUtility.hotControl == 0)))
            {
                isDragging = false;
            }
        }
        
        string FormatFloatValue(float value)
        {
            string formatted = value.ToString("F3");
            
            if (formatted.Contains("."))
            {
                formatted = formatted.TrimEnd('0');
                if (formatted.EndsWith("."))
                {
                    formatted = formatted.TrimEnd('.');
                }
            }
            
            return formatted;
        }
        
        public void DrawGlobalColorAdjustmentSliders()
        {
            var core = canvasStudio.core;
            
            HandleColorSliderWithUndo(
                "色相", 
                ref core.globalColorHue, 
                -0.5f, 0.5f, 
                ref isDraggingGlobalHue, 
                "使用テクスチャ色相調整",
                () => { needsGlobalUpdate = true; }
            );
            
            HandleColorSliderWithUndo(
                "彩度", 
                ref core.globalColorSaturation, 
                0f, 2f, 
                ref isDraggingGlobalSaturation, 
                "使用テクスチャ彩度調整",
                () => { needsGlobalUpdate = true; }
            );
            
            HandleColorSliderWithUndo(
                "明度", 
                ref core.globalColorBrightness, 
                0f, 2f, 
                ref isDraggingGlobalBrightness, 
                "使用テクスチャ明度調整",
                () => { needsGlobalUpdate = true; }
            );
            
            HandleGammaSliderWithUndo(
                "ガンマ", 
                ref core.globalColorGamma, 
                ref isDraggingGlobalGamma, 
                "使用テクスチャガンマ調整",
                () => { needsGlobalUpdate = true; }
            );
        }
        
        public void DrawPaintedAreaColorAdjustmentSliders()
        {
            var core = canvasStudio.core;
            
            HandleColorSliderWithUndo(
                "色相", 
                ref core.paintedAreaColorHue, 
                -0.5f, 0.5f, 
                ref isDraggingPaintedAreaHue, 
                "描画部分色相調整",
                () => { needsPaintedAreaUpdate = true; }
            );
            
            HandleColorSliderWithUndo(
                "彩度", 
                ref core.paintedAreaColorSaturation, 
                0f, 2f, 
                ref isDraggingPaintedAreaSaturation, 
                "描画部分彩度調整",
                () => { needsPaintedAreaUpdate = true; }
            );
            
            HandleColorSliderWithUndo(
                "明度", 
                ref core.paintedAreaColorBrightness, 
                0f, 2f, 
                ref isDraggingPaintedAreaBrightness, 
                "描画部分明度調整",
                () => { needsPaintedAreaUpdate = true; }
            );
            
            HandleGammaSliderWithUndo(
                "ガンマ", 
                ref core.paintedAreaColorGamma, 
                ref isDraggingPaintedAreaGamma, 
                "描画部分ガンマ調整",
                () => { needsPaintedAreaUpdate = true; }
            );
        }
        
public void DrawSelectionColorAdjustmentSliders()
{
    var core = canvasStudio.core;
    
    HandleColorSliderWithUndo(
        "色相", 
        ref core.colorHue, 
        -0.5f, 0.5f, 
        ref isDraggingSelectionHue, 
        "選択エリア色相調整",
        () => { 
            if (IsAnyColorAdjustmentActive())
            {
                canvasStudio.selectionSystem.IsShowingSelectionPen = false;  // ここを修正
            }
            needsColorUpdate = true; 
        }
    );
    
    HandleColorSliderWithUndo(
        "彩度", 
        ref core.colorSaturation, 
        0f, 2f, 
        ref isDraggingSelectionSaturation, 
        "選択領域彩度調整",
        () => { 
            if (IsAnyColorAdjustmentActive())
            {
                canvasStudio.selectionSystem.IsShowingSelectionPen = false;  // ここを修正
            }
            needsColorUpdate = true; 
        }
    );
    
    HandleColorSliderWithUndo(
        "明度", 
        ref core.colorBrightness, 
        0f, 2f, 
        ref isDraggingSelectionBrightness, 
        "選択領域明度調整",
        () => { 
            if (IsAnyColorAdjustmentActive())
            {
                canvasStudio.selectionSystem.IsShowingSelectionPen = false;  // ここを修正
            }
            needsColorUpdate = true; 
        }
    );
    
    HandleGammaSliderWithUndo(
        "ガンマ", 
        ref core.colorGamma, 
        ref isDraggingSelectionGamma, 
        "選択領域ガンマ調整",
        () => { 
            if (IsAnyColorAdjustmentActive())
            {
                canvasStudio.selectionSystem.IsShowingSelectionPen = false;  // ここを修正
            }
            needsColorUpdate = true; 
        }
    );
}
        
        public bool IsAnyColorAdjustmentActive()
        {
            var core = canvasStudio.core;
            return !Mathf.Approximately(core.colorHue, 0f) || 
                   !Mathf.Approximately(core.colorSaturation, 1f) || 
                   !Mathf.Approximately(core.colorBrightness, 1f) || 
                   !Mathf.Approximately(core.colorGamma, 1f);
        }
        
        public bool IsAnyPaintedAreaColorAdjustmentActive()
        {
            var core = canvasStudio.core;
            return !Mathf.Approximately(core.paintedAreaColorHue, 0f) || 
                   !Mathf.Approximately(core.paintedAreaColorSaturation, 1f) || 
                   !Mathf.Approximately(core.paintedAreaColorBrightness, 1f) || 
                   !Mathf.Approximately(core.paintedAreaColorGamma, 1f);
        }
        
        public Color ApplyColorCorrection_Liltoon(Color color, float hue, float saturation, float brightness, float gamma)
        {
            Vector3 result = new Vector3(color.r, color.g, color.b);
            
            // 1. 最初にガンマ補正を適用
            float clampedGamma = Mathf.Clamp(gamma, 0.01f, 2f);
            result.x = Mathf.Pow(Mathf.Clamp01(result.x), clampedGamma);
            result.y = Mathf.Pow(Mathf.Clamp01(result.y), clampedGamma);
            result.z = Mathf.Pow(Mathf.Clamp01(result.z), clampedGamma);
            
            // 2. HSV変換して色相・彩度を適用
            Vector3 hsv = RGB2HSV_Liltoon(result);
            
            hsv.x = (hsv.x + hue) % 1f;
            if (hsv.x < 0f) hsv.x += 1f;
            
            hsv.y = Mathf.Clamp01(hsv.y * saturation);
            
            result = HSV2RGB_Liltoon(hsv);
            
            // 3. 最後に明度を適用
            result = result * brightness;
            
            return new Color(
                Mathf.Clamp01(result.x),
                Mathf.Clamp01(result.y),
                Mathf.Clamp01(result.z),
                color.a
            );
        }
        
        Vector3 RGB2HSV_Liltoon(Vector3 color)
        {
            float max = Mathf.Max(color.x, Mathf.Max(color.y, color.z));
            float min = Mathf.Min(color.x, Mathf.Min(color.y, color.z));
            float delta = max - min;
            
            float h = 0f;
            if (delta != 0f)
            {
                if (max == color.x)
                {
                    h = ((color.y - color.z) / delta) % 6f;
                }
                else if (max == color.y)
                {
                    h = (color.z - color.x) / delta + 2f;
                }
                else
                {
                    h = (color.x - color.y) / delta + 4f;
                }
                h /= 6f;
            }
            
            if (h < 0f) h += 1f;
            
            float s = max == 0f ? 0f : delta / max;
            float v = max;
            
            return new Vector3(h, s, v);
        }
        
        Vector3 HSV2RGB_Liltoon(Vector3 hsv)
        {
            float h = hsv.x * 6f;
            float s = hsv.y;
            float v = hsv.z;
            
            int i = Mathf.FloorToInt(h);
            float f = h - i;
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));
            
            switch (i % 6)
            {
                case 0: return new Vector3(v, t, p);
                case 1: return new Vector3(q, v, p);
                case 2: return new Vector3(p, v, t);
                case 3: return new Vector3(p, q, v);
                case 4: return new Vector3(t, p, v);
                default: return new Vector3(v, p, q);
            }
        }
        
        void ApplyGlobalColorAdjustmentRealtime()
        {
            var core = canvasStudio.core;
            if (core.workingTexture == null || core.originalTextureRT == null || isProcessingGlobalColorAdjustment) return;
            
            try
            {
                isProcessingGlobalColorAdjustment = true;
                
                if (core.colorAdjustmentCS != null && core.colorAdjustmentCS.HasKernel("ApplyColorAdjustment"))
                {
                    RenderTexture tempAdjustedTexture = core.CreateOptimizedRenderTexture(core.workingTexture.width, core.workingTexture.height);
                    
                    int kernel = core.colorAdjustmentCS.FindKernel("ApplyColorAdjustment");
                    
                    core.colorAdjustmentCS.SetTexture(kernel, "InputTexture", core.originalTextureRT);
                    core.colorAdjustmentCS.SetTexture(kernel, "OutputTexture", tempAdjustedTexture);
                    
                    float clampedGlobalGamma = Mathf.Clamp(core.globalColorGamma, 0.01f, 2f);
                    
                    core.colorAdjustmentCS.SetFloat("Hue", core.globalColorHue);
                    core.colorAdjustmentCS.SetFloat("Saturation", core.globalColorSaturation);
                    core.colorAdjustmentCS.SetFloat("Brightness", core.globalColorBrightness);
                    core.colorAdjustmentCS.SetFloat("Gamma", clampedGlobalGamma);
                    core.colorAdjustmentCS.SetInt("TextureWidth", core.workingTexture.width);
                    core.colorAdjustmentCS.SetInt("TextureHeight", core.workingTexture.height);
                    
                    int dispatchX = Mathf.CeilToInt(core.workingTexture.width / 8.0f);
                    int dispatchY = Mathf.CeilToInt(core.workingTexture.height / 8.0f);
                    
                    core.colorAdjustmentCS.Dispatch(kernel, dispatchX, dispatchY, 1);
                    
                    ApplyColorAdjustmentToNonPaintedAreas(tempAdjustedTexture);
                    
                    tempAdjustedTexture.Release();
                }
                else
                {
                    ApplyGlobalColorAdjustmentCPU();
                }
                
                if (canvasStudio.selectionSystem.IsSelectionPenMode)
                {
                    canvasStudio.selectionSystem.UpdateSelectionDisplay();
                }
                else
                {
                    if (!core.isDirectTextureMode && core.targetMaterial != null)
                    {
                        canvasStudio.textureUtilities.SetMaterialPreview(core.targetMaterial, core.workingTexture);
                    }
                }
                
                SceneView.RepaintAll();
            }
            finally
            {
                isProcessingGlobalColorAdjustment = false;
            }
        }
        
        void ApplyPaintedAreaColorAdjustmentRealtime()
        {
            var core = canvasStudio.core;
            if (core.workingTexture == null || core.paintMask == null || core.originalTextureRT == null) return;
            
            try
            {
                ApplyPaintOpacity();
                
                if (canvasStudio.selectionSystem.IsSelectionPenMode)
                {
                    canvasStudio.selectionSystem.UpdateSelectionDisplay();
                }
                else
                {
                    if (!core.isDirectTextureMode && core.targetMaterial != null)
                    {
                        canvasStudio.textureUtilities.SetMaterialPreview(core.targetMaterial, core.workingTexture);
                    }
                }
                
                SceneView.RepaintAll();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"CanvasStudio: ApplyPaintedAreaColorAdjustmentRealtime エラー: {e.Message}");
            }
        }
        
        void ApplySelectionColorAdjustmentRealtime()
        {
            if (!canvasStudio.selectionSystem.IsSelectionPenMode || canvasStudio.core.workingTexture == null || canvasStudio.selectionSystem.SelectionMask == null) return;
            
            canvasStudio.selectionSystem.UpdateSelectionDisplay();
        }
        
        void ApplyPaintOpacity()
        {
            var core = canvasStudio.core;
            if (core.workingTexture == null || core.originalTextureRT == null || core.paintMask == null) return;
            
            try
            {
                if (core.brushPainterCS != null && core.brushPainterCS.HasKernel("ApplyPaintOpacity"))
                {
                    ApplyPaintOpacityGPU();
                }
                else
                {
                    ApplyPaintOpacityCPU();
                }
                
                if (canvasStudio.selectionSystem.IsSelectionPenMode)
                {
                    canvasStudio.selectionSystem.UpdateSelectionDisplay();
                }
                else
                {
                    if (!core.isDirectTextureMode && core.targetMaterial != null)
                    {
                        canvasStudio.textureUtilities.SetMaterialPreview(core.targetMaterial, core.workingTexture);
                    }
                }
                
                SceneView.RepaintAll();
                canvasStudio.Repaint();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"CanvasStudio: ApplyPaintOpacity エラー: {e.Message}");
            }
        }
        
        void ApplyPaintOpacityCPU()
        {
            var core = canvasStudio.core;
            try
            {
                Texture2D originalTex = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.originalTextureRT);
                Texture2D maskTex = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.paintMask);
                Texture2D paintColorTex = core.paintColorTexture != null ? canvasStudio.textureUtilities.RenderTextureToTexture2D(core.paintColorTexture) : null;
                
                Color[] originalPixels = originalTex.GetPixels();
                Color[] maskPixels = maskTex.GetPixels();
                Color[] paintColorPixels = paintColorTex?.GetPixels();
                Color[] workingPixels = new Color[originalPixels.Length];
                
                for (int i = 0; i < originalPixels.Length; i++)
                {
                    float maskAlpha = maskPixels[i].a;
                    
                    if (maskAlpha > CanvasStudioCore.PAINT_MASK_THRESHOLD)
                    {
                        Color paintColor;
                        
                        if (paintColorPixels != null && paintColorPixels[i].a > 0.001f)
                        {
                            paintColor = ApplyColorCorrection_Liltoon(
                                paintColorPixels[i], 
                                core.paintedAreaColorHue, 
                                core.paintedAreaColorSaturation, 
                                core.paintedAreaColorBrightness, 
                                Mathf.Clamp(core.paintedAreaColorGamma, 0.01f, 2f)
                            );
                        }
                        else
                        {
                            paintColor = originalPixels[i];
                        }
                        
float effectiveAlpha = maskAlpha * core.paintOpacity;
workingPixels[i] = PaintSystem.StrengthBasedBlend(paintColor, effectiveAlpha, originalPixels[i]);
                    }
                    else
                    {
                        workingPixels[i] = ApplyColorCorrection_Liltoon(
                            originalPixels[i], 
                            core.globalColorHue, 
                            core.globalColorSaturation, 
                            core.globalColorBrightness, 
                            Mathf.Clamp(core.globalColorGamma, 0.01f, 2f)
                        );
                    }
                }
                
                Texture2D resultTex = new Texture2D(core.workingTexture.width, core.workingTexture.height, TextureFormat.RGBA32, false);
                resultTex.SetPixels(workingPixels);
                resultTex.Apply();
                Graphics.Blit(resultTex, core.workingTexture);
                
                Object.DestroyImmediate(originalTex);
                Object.DestroyImmediate(maskTex);
                if (paintColorTex != null) Object.DestroyImmediate(paintColorTex);
                Object.DestroyImmediate(resultTex);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"CanvasStudio: ApplyPaintOpacityCPU エラー: {e.Message}");
            }
        }
        
        void ApplyPaintOpacityGPU()
        {
            var core = canvasStudio.core;
            if (core.brushPainterCS == null || !core.brushPainterCS.HasKernel("ApplyPaintOpacity")) 
            {
                ApplyPaintOpacityCPU();
                return;
            }
            
            try
            {
                RenderTexture tempAdjustedPaintTexture = core.CreateOptimizedRenderTexture(core.workingTexture.width, core.workingTexture.height);
                
                if (core.colorAdjustmentCS != null && core.colorAdjustmentCS.HasKernel("ApplyColorAdjustment"))
                {
                    int adjustKernel = core.colorAdjustmentCS.FindKernel("ApplyColorAdjustment");
                    
                    core.colorAdjustmentCS.SetTexture(adjustKernel, "InputTexture", core.paintColorTexture);
                    core.colorAdjustmentCS.SetTexture(adjustKernel, "OutputTexture", tempAdjustedPaintTexture);
                    
                    float clampedGamma = Mathf.Clamp(core.paintedAreaColorGamma, 0.01f, 2f);
                    
                    core.colorAdjustmentCS.SetFloat("Hue", core.paintedAreaColorHue);
                    core.colorAdjustmentCS.SetFloat("Saturation", core.paintedAreaColorSaturation);
                    core.colorAdjustmentCS.SetFloat("Brightness", core.paintedAreaColorBrightness);
                    core.colorAdjustmentCS.SetFloat("Gamma", clampedGamma);
                    core.colorAdjustmentCS.SetInt("TextureWidth", core.workingTexture.width);
                    core.colorAdjustmentCS.SetInt("TextureHeight", core.workingTexture.height);
                    
                    int adjustDispatchX = Mathf.CeilToInt(core.workingTexture.width / 8.0f);
                    int adjustDispatchY = Mathf.CeilToInt(core.workingTexture.height / 8.0f);
                    
                    core.colorAdjustmentCS.Dispatch(adjustKernel, adjustDispatchX, adjustDispatchY, 1);
                }
                else
                {
                    Graphics.Blit(core.paintColorTexture, tempAdjustedPaintTexture);
                }
                
                int kernel = core.brushPainterCS.FindKernel("ApplyPaintOpacity");
                
                core.brushPainterCS.SetTexture(kernel, "WorkingTexture", core.workingTexture);
                core.brushPainterCS.SetTexture(kernel, "PaintMask", core.paintMask);
                core.brushPainterCS.SetTexture(kernel, "OriginalTexture", core.originalTextureRT);
                core.brushPainterCS.SetTexture(kernel, "AdjustedTexture", tempAdjustedPaintTexture);
                core.brushPainterCS.SetFloat("PaintOpacity", core.paintOpacity);
                core.brushPainterCS.SetInt("TextureWidth", core.workingTexture.width);
                core.brushPainterCS.SetInt("TextureHeight", core.workingTexture.height);
                
                int dispatchX = Mathf.CeilToInt(core.workingTexture.width / 8.0f);
                int dispatchY = Mathf.CeilToInt(core.workingTexture.height / 8.0f);
                
                core.brushPainterCS.Dispatch(kernel, dispatchX, dispatchY, 1);
                
                tempAdjustedPaintTexture.Release();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"CanvasStudio: ApplyPaintOpacityGPU エラー: {e.Message}");
                ApplyPaintOpacityCPU();
            }
        }
        
        void ApplyGlobalColorAdjustmentCPU()
        {
            var core = canvasStudio.core;
            try
            {
                Texture2D originalTex = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.originalTextureRT);
                Texture2D workingTex = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.workingTexture);
                Texture2D maskTex = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.paintMask);
                
                Color[] originalPixels = originalTex.GetPixels();
                Color[] workingPixels = workingTex.GetPixels();
                Color[] maskPixels = maskTex.GetPixels();
                
                for (int i = 0; i < originalPixels.Length; i++)
                {
                    float maskAlpha = maskPixels[i].a;
                    
                    if (maskAlpha <= CanvasStudioCore.PAINT_MASK_THRESHOLD)
                    {
                        Color adjustedColor = ApplyColorCorrection_Liltoon(
                            originalPixels[i], 
                            core.globalColorHue, 
                            core.globalColorSaturation, 
                            core.globalColorBrightness, 
                            Mathf.Clamp(core.globalColorGamma, 0.01f, 2f)
                        );
                        workingPixels[i] = adjustedColor;
                    }
                }
                
                workingTex.SetPixels(workingPixels);
                workingTex.Apply();
                Graphics.Blit(workingTex, core.workingTexture);
                
                Object.DestroyImmediate(originalTex);
                Object.DestroyImmediate(workingTex);
                Object.DestroyImmediate(maskTex);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"CanvasStudio: ApplyGlobalColorAdjustmentCPU エラー: {e.Message}");
            }
        }
        
        void ApplyColorAdjustmentToNonPaintedAreas(RenderTexture adjustedTexture)
        {
            var core = canvasStudio.core;
            if (core.paintMask == null || adjustedTexture == null) return;
            
            if (core.brushPainterCS != null && core.brushPainterCS.HasKernel("ApplyColorAdjustmentToNonPaintedAreas"))
            {
                int kernel = core.brushPainterCS.FindKernel("ApplyColorAdjustmentToNonPaintedAreas");
                
                core.brushPainterCS.SetTexture(kernel, "WorkingTexture", core.workingTexture);
                core.brushPainterCS.SetTexture(kernel, "AdjustedTexture", adjustedTexture);
                core.brushPainterCS.SetTexture(kernel, "PaintMask", core.paintMask);
                core.brushPainterCS.SetInt("TextureWidth", core.workingTexture.width);
                core.brushPainterCS.SetInt("TextureHeight", core.workingTexture.height);
                
                int dispatchX = Mathf.CeilToInt(core.workingTexture.width / 8.0f);
                int dispatchY = Mathf.CeilToInt(core.workingTexture.height / 8.0f);
                
                core.brushPainterCS.Dispatch(kernel, dispatchX, dispatchY, 1);
            }
            else
            {
                ApplyColorAdjustmentToNonPaintedAreasCPU(adjustedTexture);
            }
        }
        
        void ApplyColorAdjustmentToNonPaintedAreasCPU(RenderTexture adjustedTexture)
        {
            var core = canvasStudio.core;
            if (core.paintMask == null || adjustedTexture == null || core.workingTexture == null) return;
            
            try
            {
                Texture2D workingTex = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.workingTexture);
                Texture2D maskTex = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.paintMask);
                Texture2D adjustedTex = canvasStudio.textureUtilities.RenderTextureToTexture2D(adjustedTexture);
                
                Color[] workingPixels = workingTex.GetPixels();
                Color[] maskPixels = maskTex.GetPixels();
                Color[] adjustedPixels = adjustedTex.GetPixels();
                
                for (int i = 0; i < workingPixels.Length; i++)
                {
                    float maskAlpha = maskPixels[i].a;
                    
                    if (maskAlpha <= CanvasStudioCore.PAINT_MASK_THRESHOLD)
                    {
                        workingPixels[i] = adjustedPixels[i];
                    }
                }
                
                workingTex.SetPixels(workingPixels);
                workingTex.Apply();
                Graphics.Blit(workingTex, core.workingTexture);
                
                Object.DestroyImmediate(workingTex);
                Object.DestroyImmediate(maskTex);
                Object.DestroyImmediate(adjustedTex);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"CanvasStudio: ApplyColorAdjustmentToNonPaintedAreasCPU エラー: {e.Message}");
            }
        }
        
        public void ApplyColorAdjustmentToTexture()
        {
            var core = canvasStudio.core;
            if (canvasStudio.selectionSystem.ColorAdjustedTexture == null) return;
            
            try
            {
                if (!core.colorAdjustmentCS.HasKernel("ApplyColorAdjustment"))
                {
                    return;
                }
                
                int kernel = core.colorAdjustmentCS.FindKernel("ApplyColorAdjustment");
                
                core.colorAdjustmentCS.SetTexture(kernel, "InputTexture", core.workingTexture);
                core.colorAdjustmentCS.SetTexture(kernel, "OutputTexture", canvasStudio.selectionSystem.ColorAdjustedTexture);
                
                float clampedGamma = Mathf.Clamp(core.colorGamma, 0.01f, 2f);
                
                core.colorAdjustmentCS.SetFloat("Hue", core.colorHue);
                core.colorAdjustmentCS.SetFloat("Saturation", core.colorSaturation);
                core.colorAdjustmentCS.SetFloat("Brightness", core.colorBrightness);
                core.colorAdjustmentCS.SetFloat("Gamma", clampedGamma);
                core.colorAdjustmentCS.SetInt("TextureWidth", core.workingTexture.width);
                core.colorAdjustmentCS.SetInt("TextureHeight", core.workingTexture.height);
                
                int dispatchX = Mathf.CeilToInt(core.workingTexture.width / 8.0f);
                int dispatchY = Mathf.CeilToInt(core.workingTexture.height / 8.0f);
                
                core.colorAdjustmentCS.Dispatch(kernel, dispatchX, dispatchY, 1);
            }
            catch (System.Exception)
            {
                Debug.LogError("CanvasStudio: ApplyColorAdjustmentToTexture エラー");
            }
        }
        
        public void ResetColorAdjustmentParameters()
        {
            var core = canvasStudio.core;
            core.colorHue = 0f;
            core.colorSaturation = 1f;
            core.colorBrightness = 1f;
            core.colorGamma = 1f;
            
            isDraggingSelectionHue = false;
            isDraggingSelectionSaturation = false;
            isDraggingSelectionBrightness = false;
            isDraggingSelectionGamma = false;
        }
        
        public void ResetGlobalColorAdjustmentParameters()
        {
            var core = canvasStudio.core;
            core.globalColorHue = 0f;
            core.globalColorSaturation = 1f;
            core.globalColorBrightness = 1f;
            core.globalColorGamma = 1f;
            
            isDraggingGlobalHue = false;
            isDraggingGlobalSaturation = false;
            isDraggingGlobalBrightness = false;
            isDraggingGlobalGamma = false;
            
            ApplyGlobalColorAdjustmentImmediate();
            canvasStudio.Repaint();
        }
        
        public void ResetPaintedAreaColorAdjustmentParameters()
        {
            var core = canvasStudio.core;
            core.paintedAreaColorHue = 0f;
            core.paintedAreaColorSaturation = 1f;
            core.paintedAreaColorBrightness = 1f;
            core.paintedAreaColorGamma = 1f;
            
            isDraggingPaintedAreaHue = false;
            isDraggingPaintedAreaSaturation = false;
            isDraggingPaintedAreaBrightness = false;
            isDraggingPaintedAreaGamma = false;
            
            ApplyPaintedAreaColorAdjustmentImmediate();
            canvasStudio.Repaint();
        }
        
        void ApplyGlobalColorAdjustmentImmediate()
        {
            needsGlobalUpdate = true;
            ApplyGlobalColorAdjustmentRealtime();
            canvasStudio.Repaint();
            SceneView.RepaintAll();
        }
        
        void ApplyPaintedAreaColorAdjustmentImmediate()
        {
            needsPaintedAreaUpdate = true;
            ApplyPaintedAreaColorAdjustmentRealtime();
            canvasStudio.Repaint();
            SceneView.RepaintAll();
        }
    }
}
