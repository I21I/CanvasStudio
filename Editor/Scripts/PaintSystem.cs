using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Unity.Profiling;

namespace CanvasStudio
{
    public class PaintSystem
    {
        private CanvasStudio canvasStudio;
        
        // プロファイラーマーカー
        private static readonly ProfilerMarker s_BrushPaintMarker = new ProfilerMarker("CanvasStudio.BrushPaint");
        private static readonly ProfilerMarker s_ComputeDispatchMarker = new ProfilerMarker("CanvasStudio.ComputeDispatch");
        private static readonly ProfilerMarker s_TextureUpdateMarker = new ProfilerMarker("CanvasStudio.TextureUpdate");
        
        public PaintSystem(CanvasStudio studio)
        {
            canvasStudio = studio;
        }
        
        public void PaintAtUV(Vector2 uvCoord, Color color, float size, float strength, bool isErase)
        {
            var core = canvasStudio.core;
            if (core.workingTexture == null) return;
            
            Color adjustedColor = GetAdjustedPaintColor();
            
            if (canvasStudio.selectionSystem.IsSelectionPenMode)
            {
                if (isErase)
                {
                    PaintSelectionErase(uvCoord);
                }
                else
                {
                    PaintSelection(uvCoord);
                }
            }
            else
            {
                if (core.useGPUBrush && core.computeShaderVerified)
                {
                    PaintGPU(uvCoord, adjustedColor, size, strength, isErase);
                }
                else
                {
                    PaintCPU(uvCoord, adjustedColor, size, strength, isErase);
                }
            }
        }
        
        public Color GetAdjustedPaintColor()
        {
            var core = canvasStudio.core;
            if (!canvasStudio.colorAdjustmentSystem.IsAnyPaintedAreaColorAdjustmentActive())
            {
                return core.paintColor;
            }
            
            return canvasStudio.colorAdjustmentSystem.ApplyColorCorrection_Liltoon(
                core.paintColor, 
                core.paintedAreaColorHue, 
                core.paintedAreaColorSaturation, 
                core.paintedAreaColorBrightness, 
                Mathf.Clamp(core.paintedAreaColorGamma, 0.01f, 2f)
            );
        }
        
        public void BucketFill(Vector2 uvCoord)
        {
            var core = canvasStudio.core;
            if (core.workingTexture == null) return;
            
            if (canvasStudio.selectionSystem.IsSelectionPenMode)
            {
                BucketFillSelection(uvCoord);
            }
            else
            {
                BucketFillNormal(uvCoord);
            }
        }
        
        void BucketFillSelection(Vector2 uvCoord)
        {
            var core = canvasStudio.core;
            var selectionMask = canvasStudio.selectionSystem.SelectionMask;
            if (selectionMask == null) return;
            
            Texture2D tempMask = canvasStudio.textureUtilities.RenderTextureToTexture2D(selectionMask);
            
            int x = Mathf.RoundToInt(uvCoord.x * (tempMask.width - 1));
            int y = Mathf.RoundToInt(uvCoord.y * (tempMask.height - 1));
            
            if (x < 0 || x >= tempMask.width || y < 0 || y >= tempMask.height) return;
            
            Color targetMask = tempMask.GetPixel(x, y);
            if (targetMask.a > CanvasStudioCore.PAINT_MASK_THRESHOLD) 
            {
                Object.DestroyImmediate(tempMask);
                return;
            }
            
            FloodFillSelectionMask(tempMask, x, y, core.brushStrength);
            
            Graphics.Blit(tempMask, selectionMask);
            Object.DestroyImmediate(tempMask);
            
            canvasStudio.selectionSystem.UpdateSelectionAreaStatus();
        }
        
        void FloodFillSelectionMask(Texture2D mask, int startX, int startY, float strength)
        {
            var core = canvasStudio.core;
            Stack<Vector2Int> stack = new Stack<Vector2Int>();
            bool[,] visited = new bool[mask.width, mask.height];
            stack.Push(new Vector2Int(startX, startY));
            
            while (stack.Count > 0)
            {
                Vector2Int pos = stack.Pop();
                
                if (pos.x < 0 || pos.x >= mask.width || pos.y < 0 || pos.y >= mask.height)
                    continue;
                
                if (visited[pos.x, pos.y]) continue;
                visited[pos.x, pos.y] = true;
                
                Color currentMask = mask.GetPixel(pos.x, pos.y);
                if (currentMask.a > CanvasStudioCore.PAINT_MASK_THRESHOLD) continue;
                
                mask.SetPixel(pos.x, pos.y, new Color(1f, 1f, 1f, strength));
                
                if (core.symmetricalPaint && core.symmetryAxisLocked)
                {
                    float axisX = core.symmetryAxisPosition * (mask.width - 1);
                    float mirrorX = 2.0f * axisX - pos.x;
                    int mirrorXInt = Mathf.RoundToInt(mirrorX);
                    
                    if (mirrorXInt >= 0 && mirrorXInt < mask.width && mirrorXInt != pos.x)
                    {
                        Color mirrorMask = mask.GetPixel(mirrorXInt, pos.y);
                        if (mirrorMask.a <= CanvasStudioCore.PAINT_MASK_THRESHOLD)
                        {
                            mask.SetPixel(mirrorXInt, pos.y, new Color(1f, 1f, 1f, strength));
                        }
                    }
                }
                
                stack.Push(new Vector2Int(pos.x + 1, pos.y));
                stack.Push(new Vector2Int(pos.x - 1, pos.y));
                stack.Push(new Vector2Int(pos.x, pos.y + 1));
                stack.Push(new Vector2Int(pos.x, pos.y - 1));
            }
            
            mask.Apply();
        }
        
        void BucketFillNormal(Vector2 uvCoord)
        {
            var core = canvasStudio.core;
            Color adjustedPaintColor = GetAdjustedPaintColor();
            
            Texture2D tempTexture = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.workingTexture);
            Texture2D tempMask = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.paintMask);
            Texture2D tempPaintColor = core.paintColorTexture != null ? canvasStudio.textureUtilities.RenderTextureToTexture2D(core.paintColorTexture) : null;
            
            int x = Mathf.RoundToInt(uvCoord.x * (tempTexture.width - 1));
            int y = Mathf.RoundToInt(uvCoord.y * (tempTexture.height - 1));
            
            if (x < 0 || x >= tempTexture.width || y < 0 || y >= tempTexture.height) return;
            
            if (core.bucketMode == 0)
            {
                Color targetColor = tempTexture.GetPixel(x, y);
                if (ColorEquals(targetColor, adjustedPaintColor, core.bucketThreshold)) 
                {
                    Object.DestroyImmediate(tempTexture);
                    Object.DestroyImmediate(tempMask);
                    if (tempPaintColor != null) Object.DestroyImmediate(tempPaintColor);
                    return;
                }
                FloodFill(tempTexture, tempMask, tempPaintColor, x, y, targetColor, adjustedPaintColor, core.brushStrength);
            }
            else
            {
                if (IsBoundaryLineCPU(tempMask, x, y)) 
                {
                    Object.DestroyImmediate(tempTexture);
                    Object.DestroyImmediate(tempMask);
                    if (tempPaintColor != null) Object.DestroyImmediate(tempPaintColor);
                    return;
                }
                
                FloodFillBoundary(tempTexture, tempMask, tempPaintColor, x, y, adjustedPaintColor, core.brushStrength);
            }
            
            Graphics.Blit(tempTexture, core.workingTexture);
            Graphics.Blit(tempMask, core.paintMask);
            
            if (tempPaintColor != null && core.paintColorTexture != null)
            {
                Graphics.Blit(tempPaintColor, core.paintColorTexture);
                Object.DestroyImmediate(tempPaintColor);
            }
            
            Object.DestroyImmediate(tempTexture);
            Object.DestroyImmediate(tempMask);
        }
        
        bool IsBoundaryLineCPU(Texture2D mask, int x, int y)
        {
            if (x < 0 || x >= mask.width || y < 0 || y >= mask.height)
                return true;
            
            Color centerMask = mask.GetPixel(x, y);
            bool centerHasPaint = centerMask.a > CanvasStudioCore.PAINT_MASK_THRESHOLD;
            
            if (!centerHasPaint)
                return false;
            
            Vector2Int[] offsets = {
                new Vector2Int(1, 0), new Vector2Int(-1, 0), 
                new Vector2Int(0, 1), new Vector2Int(0, -1)
            };
            
            foreach (var offset in offsets)
            {
                int nx = x + offset.x;
                int ny = y + offset.y;
                
                if (nx < 0 || nx >= mask.width || ny < 0 || ny >= mask.height)
                {
                    return true;
                }
                
                Color neighborMask = mask.GetPixel(nx, ny);
                bool neighborHasPaint = neighborMask.a > CanvasStudioCore.PAINT_MASK_THRESHOLD;
                
                if (!neighborHasPaint)
                    return true;
            }
            
            return false;
        }
        
        void FloodFillBoundary(Texture2D texture, Texture2D mask, Texture2D paintColorTex, int startX, int startY, Color fillColor, float strength)
        {
            var core = canvasStudio.core;
            Stack<Vector2Int> stack = new Stack<Vector2Int>();
            bool[,] visited = new bool[texture.width, texture.height];
            stack.Push(new Vector2Int(startX, startY));
            
            Color startMask = mask.GetPixel(startX, startY);
            bool startHasPaint = startMask.a > CanvasStudioCore.PAINT_MASK_THRESHOLD;
            
            Color startColor = Color.clear;
            bool useColorComparison = false;
            
            if (startHasPaint)
            {
                startColor = texture.GetPixel(startX, startY);
                useColorComparison = true;
            }
            
            float colorThreshold = startHasPaint ? 0.98f : 0.8f;
            
            Texture2D originalCopy = null;
            if (!core.originalTexture.isReadable)
            {
                originalCopy = canvasStudio.textureUtilities.CreateReadableCopy(core.originalTexture);
            }
            
            while (stack.Count > 0)
            {
                Vector2Int pos = stack.Pop();
                
                if (pos.x < 0 || pos.x >= texture.width || pos.y < 0 || pos.y >= texture.height)
                    continue;
                
                if (visited[pos.x, pos.y]) continue;
                visited[pos.x, pos.y] = true;
                
                Color currentMask = mask.GetPixel(pos.x, pos.y);
                bool currentHasPaint = currentMask.a > CanvasStudioCore.PAINT_MASK_THRESHOLD;
                
                if (startHasPaint)
                {
                    if (!currentHasPaint) continue;
                    
                    if (useColorComparison)
                    {
                        Color currentColor = texture.GetPixel(pos.x, pos.y);
                        if (!ColorEquals(currentColor, startColor, colorThreshold)) continue;
                    }
                }
                else
                {
                    if (IsBoundaryLineCPU(mask, pos.x, pos.y)) continue;
                    if (currentHasPaint) continue;
                }
                
                Color originalColor;
                if (core.originalTexture.isReadable)
                {
                    originalColor = core.originalTexture.GetPixel(pos.x, pos.y);
                }
                else
                {
                    originalColor = originalCopy.GetPixel(pos.x, pos.y);
                }
                
                Color blendedColor = StrengthBasedBlend(fillColor, strength, originalColor);
                texture.SetPixel(pos.x, pos.y, blendedColor);
                
                mask.SetPixel(pos.x, pos.y, new Color(1f, 1f, 1f, strength));
                
                if (paintColorTex != null)
                {
                    paintColorTex.SetPixel(pos.x, pos.y, core.paintColor);
                }
                
                if (core.symmetricalPaint && core.symmetryAxisLocked)
                {
                    float axisX = core.symmetryAxisPosition * (texture.width - 1);
                    float mirrorX = 2.0f * axisX - pos.x;
                    int mirrorXInt = Mathf.RoundToInt(mirrorX);
                    
                    if (mirrorXInt >= 0 && mirrorXInt < texture.width && mirrorXInt != pos.x)
                    {
                        Color mirrorMask = mask.GetPixel(mirrorXInt, pos.y);
                        bool mirrorHasPaint = mirrorMask.a > CanvasStudioCore.PAINT_MASK_THRESHOLD;
                        
                        bool shouldPaintMirror = false;
                        if (startHasPaint && mirrorHasPaint)
                        {
                            if (useColorComparison)
                            {
                                Color mirrorColor = texture.GetPixel(mirrorXInt, pos.y);
                                if (ColorEquals(mirrorColor, startColor, colorThreshold))
                                {
                                    shouldPaintMirror = true;
                                }
                            }
                            else
                            {
                                shouldPaintMirror = true;
                            }
                        }
                        else if (!startHasPaint && !mirrorHasPaint && !IsBoundaryLineCPU(mask, mirrorXInt, pos.y))
                        {
                            shouldPaintMirror = true;
                        }
                        
                        if (shouldPaintMirror)
                        {
                            Color mirrorOriginalColor;
                            if (core.originalTexture.isReadable)
                            {
                                mirrorOriginalColor = core.originalTexture.GetPixel(mirrorXInt, pos.y);
                            }
                            else
                            {
                                mirrorOriginalColor = originalCopy.GetPixel(mirrorXInt, pos.y);
                            }
                            
                            Color mirrorBlended = StrengthBasedBlend(fillColor, strength, mirrorOriginalColor);
                            texture.SetPixel(mirrorXInt, pos.y, mirrorBlended);
                            
                            mask.SetPixel(mirrorXInt, pos.y, new Color(1f, 1f, 1f, strength));
                            
                            if (paintColorTex != null)
                            {
                                paintColorTex.SetPixel(mirrorXInt, pos.y, core.paintColor);
                            }
                        }
                    }
                }
                
                stack.Push(new Vector2Int(pos.x + 1, pos.y));
                stack.Push(new Vector2Int(pos.x - 1, pos.y));
                stack.Push(new Vector2Int(pos.x, pos.y + 1));
                stack.Push(new Vector2Int(pos.x, pos.y - 1));
            }
            
            if (originalCopy != null)
            {
                Object.DestroyImmediate(originalCopy);
            }
            
            texture.Apply();
            mask.Apply();
            if (paintColorTex != null)
            {
                paintColorTex.Apply();
            }
        }
        
        void FloodFill(Texture2D texture, Texture2D mask, Texture2D paintColorTex, int startX, int startY, Color targetColor, Color fillColor, float strength)
        {
            var core = canvasStudio.core;
            Stack<Vector2Int> stack = new Stack<Vector2Int>();
            bool[,] visited = new bool[texture.width, texture.height];
            stack.Push(new Vector2Int(startX, startY));
            
            Texture2D originalCopy = null;
            if (!core.originalTexture.isReadable)
            {
                originalCopy = canvasStudio.textureUtilities.CreateReadableCopy(core.originalTexture);
            }
            
            while (stack.Count > 0)
            {
                Vector2Int pos = stack.Pop();
                
                if (pos.x < 0 || pos.x >= texture.width || pos.y < 0 || pos.y >= texture.height)
                    continue;
                
                if (visited[pos.x, pos.y]) continue;
                visited[pos.x, pos.y] = true;
                
                Color pixelColor = texture.GetPixel(pos.x, pos.y);
                if (!ColorEquals(pixelColor, targetColor, core.bucketThreshold)) continue;
                
                Color originalColor;
                if (core.originalTexture.isReadable)
                {
                    originalColor = core.originalTexture.GetPixel(pos.x, pos.y);
                }
                else
                {
                    originalColor = originalCopy.GetPixel(pos.x, pos.y);
                }
                
                Color blendedColor = StrengthBasedBlend(fillColor, strength, originalColor);
                texture.SetPixel(pos.x, pos.y, blendedColor);
                
                mask.SetPixel(pos.x, pos.y, new Color(1f, 1f, 1f, strength));
                
                if (paintColorTex != null)
                {
                    paintColorTex.SetPixel(pos.x, pos.y, core.paintColor);
                }
                
                if (core.symmetricalPaint && core.symmetryAxisLocked)
                {
                    float axisX = core.symmetryAxisPosition * (texture.width - 1);
                    float mirrorX = 2.0f * axisX - pos.x;
                    int mirrorXInt = Mathf.RoundToInt(mirrorX);
                    
                    if (mirrorXInt >= 0 && mirrorXInt < texture.width && mirrorXInt != pos.x)
                    {
                        Color mirrorOriginalColor;
                        if (core.originalTexture.isReadable)
                        {
                            mirrorOriginalColor = core.originalTexture.GetPixel(mirrorXInt, pos.y);
                        }
                        else
                        {
                            mirrorOriginalColor = originalCopy.GetPixel(mirrorXInt, pos.y);
                        }
                        
                        Color mirrorBlended = StrengthBasedBlend(fillColor, strength, mirrorOriginalColor);
                        texture.SetPixel(mirrorXInt, pos.y, mirrorBlended);
                        
                        mask.SetPixel(mirrorXInt, pos.y, new Color(1f, 1f, 1f, strength));
                        
                        if (paintColorTex != null)
                        {
                            paintColorTex.SetPixel(mirrorXInt, pos.y, core.paintColor);
                        }
                    }
                }
                
                stack.Push(new Vector2Int(pos.x + 1, pos.y));
                stack.Push(new Vector2Int(pos.x - 1, pos.y));
                stack.Push(new Vector2Int(pos.x, pos.y + 1));
                stack.Push(new Vector2Int(pos.x, pos.y - 1));
            }
            
            if (originalCopy != null)
            {
                Object.DestroyImmediate(originalCopy);
            }
            
            texture.Apply();
            mask.Apply();
            if (paintColorTex != null)
            {
                paintColorTex.Apply();
            }
        }
        
        bool ColorEquals(Color a, Color b, float threshold)
        {
            return Mathf.Abs(a.r - b.r) < threshold &&
                   Mathf.Abs(a.g - b.g) < threshold &&
                   Mathf.Abs(a.b - b.b) < threshold &&
                   Mathf.Abs(a.a - b.a) < threshold;
        }
        
        void PaintGPU(Vector2 uvCoord, Color color, float size, float strength, bool isErase)
        {
            var core = canvasStudio.core;
            if (core.brushPainterCS == null) return;
            
            try
            {
                int centerX = Mathf.RoundToInt(uvCoord.x * core.workingTexture.width);
                int centerY = Mathf.RoundToInt(uvCoord.y * core.workingTexture.height);
                
                int kernel = core.brushPainterCS.FindKernel("PaintBrush");
                
                core.brushPainterCS.SetVector("BrushCenter", new Vector2(centerX, centerY));
                core.brushPainterCS.SetFloat("BrushRadius", size);
                core.brushPainterCS.SetFloat("BrushStrength", strength);
                
                Vector4 adjustedBrushColor = new Vector4(color.r, color.g, color.b, color.a);
                Vector4 originalBrushColor = new Vector4(core.paintColor.r, core.paintColor.g, core.paintColor.b, core.paintColor.a);
                
                core.brushPainterCS.SetVector("PaintColor", adjustedBrushColor);
                core.brushPainterCS.SetVector("OriginalPaintColor", originalBrushColor);
                core.brushPainterCS.SetInt("BrushMode", 0);
                core.brushPainterCS.SetInt("EraseMode", isErase ? 1 : 0);
                core.brushPainterCS.SetInt("SymmetricalPaint", (core.symmetricalPaint && core.symmetryAxisLocked) ? 1 : 0);
                core.brushPainterCS.SetFloat("SymmetryAxisPosition", core.symmetryAxisPosition);
                core.brushPainterCS.SetInt("TextureWidth", core.workingTexture.width);
                core.brushPainterCS.SetInt("TextureHeight", core.workingTexture.height);
                
                core.brushPainterCS.SetTexture(kernel, "WorkingTexture", core.workingTexture);
                core.brushPainterCS.SetTexture(kernel, "PaintMask", core.paintMask);
                core.brushPainterCS.SetTexture(kernel, "OriginalTexture", core.originalTextureRT);
                core.brushPainterCS.SetTexture(kernel, "PaintColorTexture", core.paintColorTexture);
                
                int dispatchX = Mathf.CeilToInt(core.workingTexture.width / 8.0f);
                int dispatchY = Mathf.CeilToInt(core.workingTexture.height / 8.0f);
                
                core.brushPainterCS.Dispatch(kernel, dispatchX, dispatchY, 1);
                
            }
            catch (System.Exception)
            {
                Debug.LogError("CanvasStudio: GPU描画エラー、CPU版にフォールバック");
                PaintCPU(uvCoord, color, size, strength, isErase);
            }
        }
        
        void PaintCPU(Vector2 uvCoord, Color color, float size, float strength, bool isErase)
        {
            var core = canvasStudio.core;
            Texture2D tempTexture = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.workingTexture);
            Texture2D tempMask = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.paintMask);
            Texture2D tempPaintColor = core.paintColorTexture != null ? canvasStudio.textureUtilities.RenderTextureToTexture2D(core.paintColorTexture) : null;
            
            int centerX = Mathf.RoundToInt(uvCoord.x * tempTexture.width);
            int centerY = Mathf.RoundToInt(uvCoord.y * tempTexture.height);
            int radius = Mathf.RoundToInt(size);
            
            PaintCircleCPU(tempTexture, tempMask, tempPaintColor, centerX, centerY, radius, color, strength, isErase);
            
            Graphics.Blit(tempTexture, core.workingTexture);
            Graphics.Blit(tempMask, core.paintMask);
            
            if (tempPaintColor != null && core.paintColorTexture != null)
            {
                Graphics.Blit(tempPaintColor, core.paintColorTexture);
                Object.DestroyImmediate(tempPaintColor);
            }
            
            Object.DestroyImmediate(tempTexture);
            Object.DestroyImmediate(tempMask);
        }
        
        void PaintCircleCPU(Texture2D texture, Texture2D mask, Texture2D paintColorTex, int centerX, int centerY, int radius, Color color, float strength, bool isErase)
        {
            var core = canvasStudio.core;
            int minX = Mathf.Max(0, centerX - radius);
            int maxX = Mathf.Min(texture.width - 1, centerX + radius);
            int minY = Mathf.Max(0, centerY - radius);
            int maxY = Mathf.Min(texture.height - 1, centerY + radius);
            
            Texture2D originalCopy = null;
            if (isErase)
            {
                if (!core.originalTexture.isReadable)
                {
                    originalCopy = canvasStudio.textureUtilities.CreateReadableCopy(core.originalTexture);
                }
            }
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - centerX;
                    float dy = y - centerY;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    
                    if (distance <= radius)
                    {
                        Vector2Int pixelCoord = new Vector2Int(x, y);
                        
                        if (isErase)
                        {
                            Color originalColor;
                            if (core.originalTexture.isReadable)
                            {
                                originalColor = core.originalTexture.GetPixel(x, y);
                            }
                            else
                            {
                                originalColor = originalCopy.GetPixel(x, y);
                            }
                            
                            texture.SetPixel(x, y, originalColor);
                            mask.SetPixel(x, y, Color.clear);
                            
                            if (paintColorTex != null)
                            {
                                paintColorTex.SetPixel(x, y, Color.clear);
                            }
                            
                            core.strokePaintedPixels.Remove(pixelCoord);
                        }
                        else
                        {
                            if (core.isCurrentlyPainting && core.strokePaintedPixels.Contains(pixelCoord))
                            {
                                continue;
                            }
                            
                            Color originalColor;
                            if (core.originalTexture.isReadable)
                            {
                                originalColor = core.originalTexture.GetPixel(x, y);
                            }
                            else if (originalCopy != null)
                            {
                                originalColor = originalCopy.GetPixel(x, y);
                            }
                            else
                            {
                                originalColor = texture.GetPixel(x, y);
                            }
                            
                            Color blendedColor = StrengthBasedBlend(color, strength, originalColor);
                            texture.SetPixel(x, y, blendedColor);
                            mask.SetPixel(x, y, new Color(1f, 1f, 1f, strength));
                            
                            if (paintColorTex != null)
                            {
                                paintColorTex.SetPixel(x, y, core.paintColor);
                            }
                            
                            if (core.isCurrentlyPainting)
                            {
                                core.strokePaintedPixels.Add(pixelCoord);
                            }
                        }
                    }
                }
            }
            
            // 対称描画処理
            if (core.symmetricalPaint && core.symmetryAxisLocked)
            {
                float axisX = core.symmetryAxisPosition * (texture.width - 1);
                float mirrorCenterX = 2.0f * axisX - centerX;
                int mirrorCenterXInt = Mathf.RoundToInt(mirrorCenterX);
                
                if (mirrorCenterXInt >= 0 && mirrorCenterXInt < texture.width && mirrorCenterXInt != centerX)
                {
                    int mirrorMinX = Mathf.Max(0, mirrorCenterXInt - radius);
                    int mirrorMaxX = Mathf.Min(texture.width - 1, mirrorCenterXInt + radius);
                    
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = mirrorMinX; x <= mirrorMaxX; x++)
                        {
                            float dx = x - mirrorCenterXInt;
                            float dy = y - centerY;
                            float distance = Mathf.Sqrt(dx * dx + dy * dy);
                            
                            if (distance <= radius)
                            {
                                Vector2Int mirrorPixelCoord = new Vector2Int(x, y);
                                
                                if (isErase)
                                {
                                    Color originalColor;
                                    if (core.originalTexture.isReadable)
                                    {
                                        originalColor = core.originalTexture.GetPixel(x, y);
                                    }
                                    else
                                    {
                                        originalColor = originalCopy.GetPixel(x, y);
                                    }
                                    
                                    texture.SetPixel(x, y, originalColor);
                                    mask.SetPixel(x, y, Color.clear);
                                    
                                    if (paintColorTex != null)
                                    {
                                        paintColorTex.SetPixel(x, y, Color.clear);
                                    }
                                    
                                    core.strokePaintedPixels.Remove(mirrorPixelCoord);
                                }
                                else
                                {
                                    if (core.isCurrentlyPainting && core.strokePaintedPixels.Contains(mirrorPixelCoord))
                                    {
                                        continue;
                                    }
                                    
                                    Color originalColor;
                                    if (core.originalTexture.isReadable)
                                    {
                                        originalColor = core.originalTexture.GetPixel(x, y);
                                    }
                                    else if (originalCopy != null)
                                    {
                                        originalColor = originalCopy.GetPixel(x, y);
                                    }
                                    else
                                    {
                                        originalColor = texture.GetPixel(x, y);
                                    }
                                    
                                    Color blendedColor = StrengthBasedBlend(color, strength, originalColor);
                                    texture.SetPixel(x, y, blendedColor);
                                    mask.SetPixel(x, y, new Color(1f, 1f, 1f, strength));
                                    
                                    if (paintColorTex != null)
                                    {
                                        paintColorTex.SetPixel(x, y, core.paintColor);
                                    }
                                    
                                    if (core.isCurrentlyPainting)
                                    {
                                        core.strokePaintedPixels.Add(mirrorPixelCoord);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            if (originalCopy != null)
            {
                Object.DestroyImmediate(originalCopy);
            }
            
            texture.Apply();
            mask.Apply();
            if (paintColorTex != null)
            {
                paintColorTex.Apply();
            }
        }
        
        void PaintSelection(Vector2 uvCoord)
        {
            var core = canvasStudio.core;
            var selectionMask = canvasStudio.selectionSystem.SelectionMask;
            if (selectionMask == null) return;
            
            if (core.useGPUBrush && core.computeShaderVerified)
            {
                PaintSelectionGPU(uvCoord);
            }
            else
            {
                PaintSelectionCPU(uvCoord);
            }
        }
        
        void PaintSelectionGPU(Vector2 uvCoord)
        {
            var core = canvasStudio.core;
            var selectionMask = canvasStudio.selectionSystem.SelectionMask;
            if (core.brushPainterCS == null || selectionMask == null) return;
            
            try
            {
                int centerX = Mathf.RoundToInt(uvCoord.x * core.workingTexture.width);
                int centerY = Mathf.RoundToInt(uvCoord.y * core.workingTexture.height);
                
                int kernel = core.brushPainterCS.FindKernel("SelectionPaintOptimized");
                
                core.brushPainterCS.SetTexture(kernel, "SelectionMask", selectionMask);
                core.brushPainterCS.SetVector("SelectionBrushCenter", new Vector2(centerX, centerY));
                core.brushPainterCS.SetFloat("SelectionBrushRadius", core.brushSize);
                core.brushPainterCS.SetFloat("SelectionBrushStrength", core.brushStrength);
                core.brushPainterCS.SetInt("SelectionSymmetricalPaint", (core.symmetricalPaint && core.symmetryAxisLocked) ? 1 : 0);
                core.brushPainterCS.SetFloat("SelectionSymmetryAxisPosition", core.symmetryAxisPosition);
                core.brushPainterCS.SetInt("TextureWidth", core.workingTexture.width);
                core.brushPainterCS.SetInt("TextureHeight", core.workingTexture.height);
                
                int dispatchX = Mathf.CeilToInt(core.workingTexture.width / 8.0f);
                int dispatchY = Mathf.CeilToInt(core.workingTexture.height / 8.0f);
                
                core.brushPainterCS.Dispatch(kernel, dispatchX, dispatchY, 1);
            }
            catch (System.Exception)
            {
                PaintSelectionCPU(uvCoord);
            }
        }
        
        void PaintSelectionErase(Vector2 uvCoord)
        {
            var core = canvasStudio.core;
            var selectionMask = canvasStudio.selectionSystem.SelectionMask;
            if (selectionMask == null) return;
            
            if (core.useGPUBrush && core.computeShaderVerified && core.brushPainterCS != null)
            {
                try
                {
                    int centerX = Mathf.RoundToInt(uvCoord.x * core.workingTexture.width);
                    int centerY = Mathf.RoundToInt(uvCoord.y * core.workingTexture.height);
                    
                    int kernel = core.brushPainterCS.FindKernel("SelectionErase");
                    
                    core.brushPainterCS.SetTexture(kernel, "SelectionMask", selectionMask);
                    core.brushPainterCS.SetVector("SelectionBrushCenter", new Vector2(centerX, centerY));
                    core.brushPainterCS.SetFloat("SelectionBrushRadius", core.brushSize);
                    core.brushPainterCS.SetInt("SelectionSymmetricalPaint", (core.symmetricalPaint && core.symmetryAxisLocked) ? 1 : 0);
                    core.brushPainterCS.SetFloat("SelectionSymmetryAxisPosition", core.symmetryAxisPosition);
                    core.brushPainterCS.SetInt("TextureWidth", core.workingTexture.width);
                    core.brushPainterCS.SetInt("TextureHeight", core.workingTexture.height);
                    
                    int dispatchX = Mathf.CeilToInt(core.workingTexture.width / 8.0f);
                    int dispatchY = Mathf.CeilToInt(core.workingTexture.height / 8.0f);
                    
                    core.brushPainterCS.Dispatch(kernel, dispatchX, dispatchY, 1);
                    return;
                }
                catch (System.Exception)
                {
                    // GPU処理失敗時はCPU処理にフォールバック
                }
            }
            
            Texture2D tempMask = canvasStudio.textureUtilities.RenderTextureToTexture2D(selectionMask);
            
            int cpuCenterX = Mathf.RoundToInt(uvCoord.x * tempMask.width);
            int cpuCenterY = Mathf.RoundToInt(uvCoord.y * tempMask.height);
            int radius = Mathf.RoundToInt(core.brushSize);
            
            EraseSelectionCircleCPU(tempMask, cpuCenterX, cpuCenterY, radius);
            
            Graphics.Blit(tempMask, selectionMask);
            Object.DestroyImmediate(tempMask);
        }
        
        void PaintSelectionCPU(Vector2 uvCoord)
        {
            var core = canvasStudio.core;
            var selectionMask = canvasStudio.selectionSystem.SelectionMask;
            if (selectionMask == null) return;
            
            Texture2D tempMask = canvasStudio.textureUtilities.RenderTextureToTexture2D(selectionMask);
            
            int centerX = Mathf.RoundToInt(uvCoord.x * tempMask.width);
            int centerY = Mathf.RoundToInt(uvCoord.y * tempMask.height);
            int radius = Mathf.RoundToInt(core.brushSize);
            
            PaintSelectionCircleCPU(tempMask, centerX, centerY, radius, core.brushStrength);
            
            Graphics.Blit(tempMask, selectionMask);
            Object.DestroyImmediate(tempMask);
        }
        
        void PaintSelectionCircleCPU(Texture2D mask, int centerX, int centerY, int radius, float strength)
        {
            var core = canvasStudio.core;
            int minX = Mathf.Max(0, centerX - radius);
            int maxX = Mathf.Min(mask.width - 1, centerX + radius);
            int minY = Mathf.Max(0, centerY - radius);
            int maxY = Mathf.Min(mask.height - 1, centerY + radius);
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - centerX;
                    float dy = y - centerY;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    
                    if (distance <= radius)
                    {
                        Vector2Int pixelCoord = new Vector2Int(x, y);
                        
                        if (core.isCurrentlyPainting && core.strokePaintedPixels.Contains(pixelCoord))
                        {
                            continue;
                        }
                        
                        mask.SetPixel(x, y, new Color(1f, 1f, 1f, strength));
                        
                        if (core.isCurrentlyPainting)
                        {
                            core.strokePaintedPixels.Add(pixelCoord);
                        }
                    }
                }
            }
            
            // 対称描画処理
            if (core.symmetricalPaint && core.symmetryAxisLocked)
            {
                float axisX = core.symmetryAxisPosition * (mask.width - 1);
                float mirrorCenterX = 2.0f * axisX - centerX;
                int mirrorCenterXInt = Mathf.RoundToInt(mirrorCenterX);
                
                if (mirrorCenterXInt >= 0 && mirrorCenterXInt < mask.width && mirrorCenterXInt != centerX)
                {
                    int mirrorMinX = Mathf.Max(0, mirrorCenterXInt - radius);
                    int mirrorMaxX = Mathf.Min(mask.width - 1, mirrorCenterXInt + radius);
                    
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = mirrorMinX; x <= mirrorMaxX; x++)
                        {
                            float dx = x - mirrorCenterXInt;
                            float dy = y - centerY;
                            float distance = Mathf.Sqrt(dx * dx + dy * dy);
                            
                            if (distance <= radius)
                            {
                                Vector2Int mirrorPixelCoord = new Vector2Int(x, y);
                                
                                if (core.isCurrentlyPainting && core.strokePaintedPixels.Contains(mirrorPixelCoord))
                                {
                                    continue;
                                }
                                
                                mask.SetPixel(x, y, new Color(1f, 1f, 1f, strength));
                                
                                if (core.isCurrentlyPainting)
                                {
                                    core.strokePaintedPixels.Add(mirrorPixelCoord);
                                }
                            }
                        }
                    }
                }
            }
            
            mask.Apply();
        }
        
        void EraseSelectionCircleCPU(Texture2D mask, int centerX, int centerY, int radius)
        {
            var core = canvasStudio.core;
            int minX = Mathf.Max(0, centerX - radius);
            int maxX = Mathf.Min(mask.width - 1, centerX + radius);
            int minY = Mathf.Max(0, centerY - radius);
            int maxY = Mathf.Min(mask.height - 1, centerY + radius);
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - centerX;
                    float dy = y - centerY;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    
                    if (distance <= radius)
                    {
                        mask.SetPixel(x, y, Color.clear);
                    }
                }
            }
            
            if (core.symmetricalPaint && core.symmetryAxisLocked)
            {
                float axisX = core.symmetryAxisPosition * (mask.width - 1);
                float mirrorCenterX = 2.0f * axisX - centerX;
                int mirrorCenterXInt = Mathf.RoundToInt(mirrorCenterX);
                
                if (mirrorCenterXInt >= 0 && mirrorCenterXInt < mask.width && mirrorCenterXInt != centerX)
                {
                    int mirrorMinX = Mathf.Max(0, mirrorCenterXInt - radius);
                    int mirrorMaxX = Mathf.Min(mask.width - 1, mirrorCenterXInt + radius);
                    
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = mirrorMinX; x <= mirrorMaxX; x++)
                        {
                            float dx = x - mirrorCenterXInt;
                            float dy = y - centerY;
                            float distance = Mathf.Sqrt(dx * dx + dy * dy);
                            
                            if (distance <= radius)
                            {
                                mask.SetPixel(x, y, Color.clear);
                            }
                        }
                    }
                }
            }
            
            mask.Apply();
        }
        
        public static Color StrengthBasedBlend(Color srcColor, float strength, Color dstColor)
        {
            Color src = new Color(srcColor.r, srcColor.g, srcColor.b, strength);
            
            float outAlpha = src.a + dstColor.a * (1f - src.a);
            
            if (outAlpha <= 0.001f)
            {
                return Color.clear;
            }
            
            Vector3 outRGB = new Vector3(
                src.r * src.a + dstColor.r * dstColor.a * (1f - src.a),
                src.g * src.a + dstColor.g * dstColor.a * (1f - src.a),
                src.b * src.a + dstColor.b * dstColor.a * (1f - src.a)
            ) / outAlpha;
            
            return new Color(outRGB.x, outRGB.y, outRGB.z, outAlpha);
        }
        
        public void ClearNormalPaintedAreasImmediate()
        {
            var core = canvasStudio.core;
            if (core.workingTexture == null || core.originalTextureRT == null || core.paintMask == null) return;
            
            canvasStudio.undoSystem.SaveUnifiedUndoState("描画部分クリア", UnifiedUndoType.TextureOperation, true);
            
            using (s_TextureUpdateMarker.Auto())
            {
                if (core.clearShaderCS != null && core.useGPUBrush)
                {
                    try
                    {
                        int kernel = core.clearShaderCS.FindKernel("ClearWithMask");
                        
                        core.clearShaderCS.SetTexture(kernel, "TargetTexture", core.workingTexture);
                        core.clearShaderCS.SetTexture(kernel, "OriginalTexture", core.originalTextureRT);
                        core.clearShaderCS.SetTexture(kernel, "MaskTexture", core.paintMask);
                        core.clearShaderCS.SetInt("TextureWidth", core.workingTexture.width);
                        core.clearShaderCS.SetInt("TextureHeight", core.workingTexture.height);
                        
                        int groupsX = Mathf.CeilToInt((float)core.workingTexture.width / 8);
                        int groupsY = Mathf.CeilToInt((float)core.workingTexture.height / 8);
                        
                        core.clearShaderCS.Dispatch(kernel, groupsX, groupsY, 1);
                    }
                    catch (System.Exception)
                    {
                        Graphics.Blit(core.originalTextureRT, core.workingTexture);
                    }
                }
                else
                {
                    Graphics.Blit(core.originalTextureRT, core.workingTexture);
                }
                
                RenderTexture.active = core.paintMask;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = null;

                if (core.paintColorTexture != null)
                {
                    RenderTexture.active = core.paintColorTexture;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = null;
                }
                
                if (canvasStudio.selectionSystem.IsSelectionPenMode && canvasStudio.selectionSystem.NormalModeTexture != null && canvasStudio.selectionSystem.NormalModeMask != null)
                {
                    Graphics.Blit(core.originalTextureRT, canvasStudio.selectionSystem.NormalModeTexture);
                    RenderTexture.active = canvasStudio.selectionSystem.NormalModeMask;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = null;
                }
                
                UpdateDisplayAfterAction();
            }
        }
        
        void UpdateDisplayAfterAction()
        {
            var core = canvasStudio.core;
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
            
            EditorUtility.SetDirty(canvasStudio);
            SceneView.RepaintAll();
            canvasStudio.Repaint();
        }
        
        public bool HasNormalPaintedAreas()
        {
            return canvasStudio.core.paintMask != null;
        }
        
        public void ProcessTextureInvert(int target)
        {
            var core = canvasStudio.core;
            if (core.workingTexture == null) return;
            
            canvasStudio.undoSystem.SaveUnifiedUndoState("色反転処理", UnifiedUndoType.TextureOperation, true);
            
            Texture2D tempTexture = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.workingTexture);
            Texture2D tempMask = canvasStudio.textureUtilities.RenderTextureToTexture2D(core.paintMask);
            
            Color[] pixels = tempTexture.GetPixels();
            Color[] maskPixels = tempMask.GetPixels();
            
            for (int i = 0; i < pixels.Length; i++)
            {
                bool shouldProcess = false;
                
                switch (target)
                {
                    case 0:
                        shouldProcess = maskPixels[i].a > CanvasStudioCore.PAINT_MASK_THRESHOLD;
                        break;
                    case 1:
                        shouldProcess = maskPixels[i].a <= CanvasStudioCore.PAINT_MASK_THRESHOLD;
                        break;
                    case 2:
                        shouldProcess = true;
                        break;
                }
                
                if (shouldProcess)
                {
                    pixels[i] = new Color(
                        1f - pixels[i].r,
                        1f - pixels[i].g, 
                        1f - pixels[i].b, 
                        pixels[i].a
                    );
                }
            }
            
            tempTexture.SetPixels(pixels);
            tempTexture.Apply();
            Graphics.Blit(tempTexture, core.workingTexture);
            
            Object.DestroyImmediate(tempTexture);
            Object.DestroyImmediate(tempMask);
            
            if (!core.isDirectTextureMode && core.targetMaterial != null)
            {
                canvasStudio.textureUtilities.SetMaterialPreview(core.targetMaterial, core.workingTexture);
            }
            
            SceneView.RepaintAll();
        }
    }
}
