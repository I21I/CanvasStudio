using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;

namespace CanvasStudio
{
    // 統一Undo用の操作タイプ
    public enum UnifiedUndoType
    {
        TextureOperation,      // テクスチャ操作（ブラシ、色調補正など）
        ParameterOperation     // パラメータ操作（スライダーなど）
    }

    // 統一Undo用の操作記録
    [System.Serializable]
    public class UnifiedUndoRecord
    {
        public UnifiedUndoType operationType;
        public string operationName;
        public UnifiedUndoState undoState;
        public System.DateTime timestamp;
    }

    // 統一Undo用のデータクラス
    [System.Serializable]
    public class UnifiedUndoState
    {
        // テクスチャ状態
        public RenderTexture workingTexture;
        public RenderTexture paintMask;
        public RenderTexture selectionMask;
        public RenderTexture originalTextureRT;
        public RenderTexture paintColorTexture;
        
        // パラメータ状態
        public float hue, saturation, brightness, gamma;
        public float globalHue, globalSaturation, globalBrightness, globalGamma;
        public float paintedAreaHue, paintedAreaSaturation, paintedAreaBrightness, paintedAreaGamma;
        public float symmetryAxis;
        public float brushStrength;
        public float paintOpacity;
        public bool wasSelectionMode;
        public bool wasShowingSelection;
        public string operationName;
        
        // ターゲット情報
        public GameObject savedTargetObject;
        public Material savedTargetMaterial;
        public Texture2D savedDirectTexture;
        public Texture2D savedOriginalTexture;
        public bool savedIsDirectTextureMode;
        public int savedMaterialIndex;
        
        public void Release()
        {
            if (workingTexture != null) { workingTexture.Release(); workingTexture = null; }
            if (paintMask != null) { paintMask.Release(); paintMask = null; }
            if (selectionMask != null) { selectionMask.Release(); selectionMask = null; }
            if (originalTextureRT != null) { originalTextureRT.Release(); originalTextureRT = null; }
            if (paintColorTexture != null) { paintColorTexture.Release(); paintColorTexture = null; }
        }
        
        public UnifiedUndoState DeepCopy()
        {
            UnifiedUndoState copy = new UnifiedUndoState();
            copy.operationName = operationName;
            copy.hue = hue;
            copy.saturation = saturation;
            copy.brightness = brightness;
            copy.gamma = gamma;
            copy.globalHue = globalHue;
            copy.globalSaturation = globalSaturation;
            copy.globalBrightness = globalBrightness;
            copy.globalGamma = globalGamma;
            copy.paintedAreaHue = paintedAreaHue;
            copy.paintedAreaSaturation = paintedAreaSaturation;
            copy.paintedAreaBrightness = paintedAreaBrightness;
            copy.paintedAreaGamma = paintedAreaGamma;
            copy.symmetryAxis = symmetryAxis;
            copy.brushStrength = brushStrength;
            copy.paintOpacity = paintOpacity;
            copy.wasSelectionMode = wasSelectionMode;
            copy.wasShowingSelection = wasShowingSelection;
            
            // ターゲット情報のコピー
            copy.savedTargetObject = savedTargetObject;
            copy.savedTargetMaterial = savedTargetMaterial;
            copy.savedDirectTexture = savedDirectTexture;
            copy.savedOriginalTexture = savedOriginalTexture;
            copy.savedIsDirectTextureMode = savedIsDirectTextureMode;
            copy.savedMaterialIndex = savedMaterialIndex;
            
            return copy;
        }
    }

    public partial class CanvasStudio : EditorWindow
    {
        // 基本的なターゲット管理
        protected GameObject targetObject;
        protected Renderer targetRenderer;
        protected Material targetMaterial;
        protected Material originalMaterial;
        protected int materialIndex = 0;
        
        // テクスチャ直接編集用
        protected Texture2D directTexture;
        protected bool isDirectTextureMode = false;
        
        protected Texture2D originalTexture;
        protected RenderTexture workingTexture;
        protected RenderTexture paintMask;
        protected RenderTexture originalTextureRT;
        protected RenderTexture paintColorTexture;
        
        // 色調補正システム
        [SerializeField] protected bool selectionPenMode = false;
        protected ComputeShader colorAdjustmentCS;
        protected RenderTexture selectionPreviewTexture;
        protected RenderTexture colorAdjustedTexture;
        [SerializeField] protected bool isShowingSelectionPen = true;
        
        // 選択ペンモード用
        protected RenderTexture normalModeMask;
        protected RenderTexture normalModeTexture;
        protected RenderTexture selectionMask;
        [SerializeField] protected Texture2D backupTexture;
        [SerializeField] protected Texture2D persistentSelectionMask;
        
        // 描画部分色調補正パラメータ
        [SerializeField] protected float paintedAreaColorHue = 0f;
        [SerializeField] protected float paintedAreaColorSaturation = 1f;
        [SerializeField] protected float paintedAreaColorBrightness = 1f;
        [SerializeField] protected float paintedAreaColorGamma = 1f;

        // 参照テクスチャ色調補正パラメータ
        [SerializeField] protected float globalColorHue = 0f;
        [SerializeField] protected float globalColorSaturation = 1f;
        [SerializeField] protected float globalColorBrightness = 1f;
        [SerializeField] protected float globalColorGamma = 1f;

        // 色調補正パラメータ
        [SerializeField] protected float colorHue = 0f;
        [SerializeField] protected float colorSaturation = 1f;
        [SerializeField] protected float colorBrightness = 1f;
        [SerializeField] protected float colorGamma = 1f;
        
        // 最適化用フラグ
        protected bool needsColorUpdate = false;
        protected bool needsGlobalUpdate = false;
        protected bool needsPaintedAreaUpdate = false;
        protected bool isProcessingGlobalColorAdjustment = false;
        protected bool needsPaintOpacityUpdate = false;
        
        // GPU ブラシ関連
        protected ComputeShader brushPainterCS;
        protected ComputeShader clearShaderCS;
        protected bool useGPUBrush = false;
        protected bool computeShaderVerified = false;
        
        // ペイント設定
        protected Color paintColor = Color.red;
        protected float brushSize = 20f;
        protected float brushStrength = 1f;
        protected float paintOpacity = 1f;
        protected bool eraseMode = false;  
        protected bool symmetricalPaint = false;
        protected bool useBucketTool = false;
        
        // 対称軸設定
        protected float symmetryAxisPosition = 0.5f;
        protected bool symmetryAxisLocked = true;
        protected bool isDraggingSymmetryAxis = false;
        
        // スライダードラッグ状態の管理
        protected bool isDraggingGlobalHue = false;
        protected bool isDraggingGlobalSaturation = false;
        protected bool isDraggingGlobalBrightness = false;
        protected bool isDraggingGlobalGamma = false;
        protected bool isDraggingSelectionHue = false;
        protected bool isDraggingSelectionSaturation = false;
        protected bool isDraggingSelectionBrightness = false;
        protected bool isDraggingSelectionGamma = false;
        protected bool isDraggingPaintedAreaHue = false;
        protected bool isDraggingPaintedAreaSaturation = false;
        protected bool isDraggingPaintedAreaBrightness = false;
        protected bool isDraggingPaintedAreaGamma = false;
        protected bool isDraggingBrushSize = false;
        protected bool isDraggingBrushStrength = false;
        protected bool isDraggingPaintOpacity = false;
        
        // 統一Undoシステム
        protected List<UnifiedUndoRecord> undoStack = new List<UnifiedUndoRecord>();
        protected List<UnifiedUndoRecord> redoStack = new List<UnifiedUndoRecord>();
        protected const int maxUndoSteps = 50;
        protected bool isRestoringFromUndo = false;
        
        // バケツツール設定
        protected int bucketMode = 1;
        protected float bucketThreshold = 0.5f;
        
        // エクスポート設定
        protected bool exportFullTexture = false;
        protected int backgroundMode = 0;
        
        // マテリアル復元用
        protected Dictionary<Material, Dictionary<string, Texture>> originalShaderTextures = new Dictionary<Material, Dictionary<string, Texture>>();
        protected bool isPreviewActive = false;
        protected bool preventTextureRestore = false;
        protected bool needsMaterialRestore = false;
        
        // 表示ボタン押下時の自動選択を防ぐフラグ
        protected bool ignoreSelectionChange = false;
        protected float ignoreSelectionChangeUntil = 0f;
        protected float lastShowButtonTime = 0f;
        protected const float SHOW_BUTTON_COOLDOWN = 0.1f;
        
        // プロファイラーマーカー
        protected static readonly ProfilerMarker s_BrushPaintMarker = new ProfilerMarker("CanvasStudio.BrushPaint");
        protected static readonly ProfilerMarker s_ComputeDispatchMarker = new ProfilerMarker("CanvasStudio.ComputeDispatch");
        protected static readonly ProfilerMarker s_TextureUpdateMarker = new ProfilerMarker("CanvasStudio.TextureUpdate");
        
        // UI関連
        protected Vector2 leftScrollPosition;
        protected Vector2 rightScrollPosition;
        protected bool isPainting = false;
        protected bool showPaintSettings = true;
        protected bool showColorSettings = true;
        protected bool showColorAdjustmentSettings = true;
        protected bool showSelectionColorAdjustmentSettings = true;
        protected bool showUVMesh = true;

        protected Color customBackgroundColor = new Color(1f, 1f, 1f, 0f);
        protected Color customMeshColor = Color.black;
        protected bool autoSetAfterApply = true;
        
        // 色調補正サブタブの状態
        protected bool showPaintedAreaColorAdjustment = true;
        protected bool showReferenceColorAdjustment = false;
        protected bool showCombinedColorAdjustment = false;
        
        // レイアウト定数
        protected const float leftPanelWidth = 350f;
        protected const float operationAreaHeight = 75f;
        
        // UV表示関連
        protected float uvZoom = 1f;
        protected Vector2 uvPanOffset = Vector2.zero;
        protected const float baseUVSize = 540f;
        protected const float minZoom = 0.1f;
        protected const float maxZoom = 10f;
        protected bool isDraggingUV = false;
        protected Vector2 lastMousePosition;
        protected Vector2 lastPaintPosition;
        protected bool hasLastPaintPosition = false;
        protected Vector2 straightLineStart;
        protected bool isDrawingStraightLine = false;
        
        // カーソル
        protected Texture2D brushCursorTexture;
        protected Texture2D bucketCursorTexture;
        protected Rect lastPreviewRect;
        
        // メッシュ表示
        protected ComputeShader meshGeneratorCS;
        protected RenderTexture meshTexture;
        protected bool meshTextureDirty = true;
        protected GameObject lastMeshCachedObject;
        
        // 定数
        protected const float PAINT_MASK_THRESHOLD = 0.001f;
        protected static readonly Color SELECTION_PEN_COLOR = new Color(0.4f, 0.4f, 1.0f, 0.6f);
        
        // その他のモード・状態
        protected bool isSpoidMode = false;
        protected bool hasSelectionArea = false;
        protected HashSet<Vector2Int> strokePaintedPixels = new HashSet<Vector2Int>();
        protected bool isCurrentlyPainting = false;
        protected Texture2D checkerboardTexture;
        
        // シェーダー対応の定数
        protected static readonly string[] POIYOMI_TEXTURE_PROPERTIES = {
            "_MainTex", "_BaseMap", "_AlbedoMap", "_DiffuseMap", "_ColorMap", "_BaseColorMap"
        };
        
        protected static readonly string[] LILTOON_TEXTURE_PROPERTIES = {
            "_MainTex", "_BaseMap", "_BaseColorMap"
        };
        
        protected static readonly string[] UNITY_STANDARD_TEXTURE_PROPERTIES = {
            "_MainTex", "_BaseMap", "_BaseColorMap", "_AlbedoMap"
        };
        
        protected static readonly string[] VRCHAT_TEXTURE_PROPERTIES = {
            "_MainTex", "_Diffuse", "_DiffuseTexture", "_Albedo"
        };

        void ResetAllState()
        {
            try
            {
                // 前の状態を完全にクリーンアップ
                if (targetObject != null || targetRenderer != null || targetMaterial != null || isDirectTextureMode)
                {
                    RestoreAllMaterials();
                }
                
                CleanupAllResources();
                
                // 全てのターゲット関連をクリア
                targetObject = null;
                targetRenderer = null;
                targetMaterial = null;
                originalMaterial = null;
                originalTexture = null;
                directTexture = null;
                isDirectTextureMode = false;
                materialIndex = 0;
                
                // 選択ペンモード関連をクリア
                selectionPenMode = false;
                isShowingSelectionPen = true;
                hasSelectionArea = false;
                
                // 色調補正パラメータをリセット
                colorHue = 0f;
                colorSaturation = 1f;
                colorBrightness = 1f;
                colorGamma = 1f;
                globalColorHue = 0f;
                globalColorSaturation = 1f;
                globalColorBrightness = 1f;
                globalColorGamma = 1f;
                paintedAreaColorHue = 0f;
                paintedAreaColorSaturation = 1f;
                paintedAreaColorBrightness = 1f;
                paintedAreaColorGamma = 1f;
                
                // ペイント設定をリセット
                paintColor = Color.red;
                brushSize = 20f;
                brushStrength = 1f;
                paintOpacity = 1f;
                eraseMode = false;
                symmetricalPaint = false;
                useBucketTool = false;
                symmetryAxisPosition = 0.5f;
                symmetryAxisLocked = true;
                
                // UI状態をリセット
                uvZoom = 1f;
                uvPanOffset = Vector2.zero;
                showPaintSettings = true;
                showColorSettings = true;
                showColorAdjustmentSettings = true;
                showSelectionColorAdjustmentSettings = true;
                showUVMesh = true;
                exportFullTexture = false;
                backgroundMode = 0;
                customBackgroundColor = new Color(1f, 1f, 1f, 0f);
                
                // 色調補正サブタブの状態をリセット
                showPaintedAreaColorAdjustment = true;
                showReferenceColorAdjustment = false;
                showCombinedColorAdjustment = false;
                
                // ドラッグ状態をクリア
                isPainting = false;
                isDraggingSymmetryAxis = false;
                isDraggingUV = false;
                isDrawingStraightLine = false;
                hasLastPaintPosition = false;
                isCurrentlyPainting = false;
                
                // ドラッグフラグをクリア
                isDraggingGlobalHue = false;
                isDraggingGlobalSaturation = false;
                isDraggingGlobalBrightness = false;
                isDraggingGlobalGamma = false;
                isDraggingSelectionHue = false;
                isDraggingSelectionSaturation = false;
                isDraggingSelectionBrightness = false;
                isDraggingSelectionGamma = false;
                isDraggingPaintedAreaHue = false;
                isDraggingPaintedAreaSaturation = false;
                isDraggingPaintedAreaBrightness = false;
                isDraggingPaintedAreaGamma = false;
                isDraggingBrushSize = false;
                isDraggingBrushStrength = false;
                
                // その他の状態フラグをリセット
                needsColorUpdate = false;
                needsGlobalUpdate = false;
                needsPaintedAreaUpdate = false;
                isProcessingGlobalColorAdjustment = false;
                preventTextureRestore = false;
                isPreviewActive = false;
                needsMaterialRestore = false;
                meshTextureDirty = true;
                lastMeshCachedObject = null;
                isSpoidMode = false;
                isRestoringFromUndo = false;
                ignoreSelectionChange = false;
                ignoreSelectionChangeUntil = 0f;
                lastShowButtonTime = 0f;
                autoSetAfterApply = true;
                
                // Undoスタックをクリア
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
                
                // バックアップテクスチャをクリア
                if (backupTexture != null)
                {
                    DestroyImmediate(backupTexture);
                    backupTexture = null;
                }
                
                if (persistentSelectionMask != null)
                {
                    DestroyImmediate(persistentSelectionMask);
                    persistentSelectionMask = null;
                }
                
                // エディターダーティフラグをクリア
                EditorUtility.SetDirty(this);
                
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Canvas Studio: ResetAllState エラー: {e.Message}");
            }
        }

        void CleanupAllResources()
        {
            RenderTexture.active = null;
            CleanupTextures();
            
            if (brushCursorTexture != null) 
            { 
                try 
                { 
                    DestroyImmediate(brushCursorTexture); 
                } 
                catch (System.Exception) 
                { 
                } 
                brushCursorTexture = null; 
            }
            
            if (bucketCursorTexture != null) 
            { 
                try 
                { 
                    DestroyImmediate(bucketCursorTexture); 
                } 
                catch (System.Exception) 
                { 
                } 
                bucketCursorTexture = null; 
            }
            
            if (checkerboardTexture != null)
            {
                try 
                { 
                    DestroyImmediate(checkerboardTexture); 
                } 
                catch (System.Exception) 
                { 
                } 
                checkerboardTexture = null;
            }
            
            if (meshTexture != null) { meshTexture.Release(); meshTexture = null; }
            
            targetObject = null;
            targetRenderer = null;
            targetMaterial = null;
            originalMaterial = null;
            originalTexture = null;
            directTexture = null;
            isDirectTextureMode = false;
            
            selectionPenMode = false;
            UpdateSelectionAreaStatus();
            
            ResetColorAdjustmentParameters();
            ResetGlobalColorAdjustmentParameters();
            ResetPaintedAreaColorAdjustmentParameters();
            
            if (backupTexture != null)
            {
                DestroyImmediate(backupTexture);
                backupTexture = null;
            }
            
            if (persistentSelectionMask != null)
            {
                DestroyImmediate(persistentSelectionMask);
                persistentSelectionMask = null;
            }
        }

        void CleanupTextures()
        {
            // RenderTexture.active を安全にリセット
            RenderTexture.active = null;
            
            if (workingTexture != null) { workingTexture.Release(); workingTexture = null; }
            if (paintMask != null) { paintMask.Release(); paintMask = null; }
            if (originalTextureRT != null) { originalTextureRT.Release(); originalTextureRT = null; }
            if (paintColorTexture != null) { paintColorTexture.Release(); paintColorTexture = null; }
            if (selectionPreviewTexture != null) { selectionPreviewTexture.Release(); selectionPreviewTexture = null; }
            if (colorAdjustedTexture != null) { colorAdjustedTexture.Release(); colorAdjustedTexture = null; }
            if (normalModeTexture != null) { normalModeTexture.Release(); normalModeTexture = null; }
            if (normalModeMask != null) { normalModeMask.Release(); normalModeMask = null; }
            if (selectionMask != null) { selectionMask.Release(); selectionMask = null; }
        }
    }
}
