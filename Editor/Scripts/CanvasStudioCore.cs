using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CanvasStudio
{
    public class CanvasStudioCore
    {
        // 基本的なターゲット管理
        public GameObject targetObject;
        public Renderer targetRenderer;
        public Material targetMaterial;
        public Material originalMaterial;
        public int materialIndex = 0;
        
        // テクスチャ直接編集用
        public Texture2D directTexture;
        public bool isDirectTextureMode = false;
        
        // レンダーテクスチャ
        public Texture2D originalTexture;
        public RenderTexture workingTexture;
        public RenderTexture paintMask;
        public RenderTexture originalTextureRT;
        public RenderTexture paintColorTexture;
        
        // ComputeShader
        public ComputeShader colorAdjustmentCS;
        public ComputeShader brushPainterCS;
        public ComputeShader clearShaderCS;
        public ComputeShader meshGeneratorCS;
        
        // ペイント設定
        public Color paintColor = Color.red;
        public float brushSize = 20f;
        public float brushStrength = 1f;
        public float paintOpacity = 1f;
        public bool eraseMode = false;
        public bool symmetricalPaint = false;
        public bool useBucketTool = false;
        
        // 対称軸設定
        public float symmetryAxisPosition = 0.5f;
        public bool symmetryAxisLocked = true;
        public bool isDraggingSymmetryAxis = false;
        
        // 色調補正パラメータ
        public float paintedAreaColorHue = 0f;
        public float paintedAreaColorSaturation = 1f;
        public float paintedAreaColorBrightness = 1f;
        public float paintedAreaColorGamma = 1f;
        
        public float globalColorHue = 0f;
        public float globalColorSaturation = 1f;
        public float globalColorBrightness = 1f;
        public float globalColorGamma = 1f;
        
        public float colorHue = 0f;
        public float colorSaturation = 1f;
        public float colorBrightness = 1f;
        public float colorGamma = 1f;
        
        // UI状態
        public bool showPaintSettings = true;
        public bool showColorSettings = true;
        public bool showColorAdjustmentSettings = true;
        public bool showSelectionColorAdjustmentSettings = true;
        public bool showUVMesh = true;
        public bool showPaintedAreaColorAdjustment = true;
        public bool showReferenceColorAdjustment = false;
        public bool showCombinedColorAdjustment = false;
        
        // バケツツール設定
        public int bucketMode = 1;
        public float bucketThreshold = 0.5f;
        
        // エクスポート設定
        public bool exportFullTexture = false;
        public int backgroundMode = 0;
        public Color customBackgroundColor = new Color(1f, 1f, 1f, 0f);
        public Color customMeshColor = Color.black;
        public bool autoSetAfterApply = true;
        
        // GPU設定
        public bool useGPUBrush = false;
        public bool computeShaderVerified = false;
        
        // UV表示関連
        public float uvZoom = 1f;
        public Vector2 uvPanOffset = Vector2.zero;
        public const float baseUVSize = 540f;
        public const float minZoom = 0.1f;
        public const float maxZoom = 10f;
        public bool isDraggingUV = false;
        public Vector2 lastMousePosition;
        
        // ペイント状態
        public bool isPainting = false;
        public Vector2 lastPaintPosition;
        public bool hasLastPaintPosition = false;
        public Vector2 straightLineStart;
        public bool isDrawingStraightLine = false;
        public bool isCurrentlyPainting = false;
        public HashSet<Vector2Int> strokePaintedPixels = new HashSet<Vector2Int>();
        
        // カーソルテクスチャ
        public Texture2D brushCursorTexture;
        public Texture2D bucketCursorTexture;
        public Texture2D checkerboardTexture;
        
        // メッシュ表示
        public RenderTexture meshTexture;
        public bool meshTextureDirty = true;
        public GameObject lastMeshCachedObject;
        
        // スクロール位置
        public Vector2 leftScrollPosition;
        public Vector2 rightScrollPosition;
        
        // フラグ
        public bool isSpoidMode = false;
        public bool preventTextureRestore = false;
        public bool needsMaterialRestore = false;
        public bool isPreviewActive = false;
        
        // 定数
        public const float leftPanelWidth = 350f;
        public const float operationAreaHeight = 75f;
        public const float PAINT_MASK_THRESHOLD = 0.001f;
        public static readonly Color SELECTION_PEN_COLOR = new Color(0.4f, 0.4f, 1.0f, 0.6f);
        
        // マテリアル復元用
        public Dictionary<Material, Dictionary<string, Texture>> originalShaderTextures = new Dictionary<Material, Dictionary<string, Texture>>();
        
        // シェーダー対応
        public static readonly string[] POIYOMI_TEXTURE_PROPERTIES = {
            "_MainTex", "_BaseMap", "_AlbedoMap", "_DiffuseMap", "_ColorMap", "_BaseColorMap"
        };
        
        public static readonly string[] LILTOON_TEXTURE_PROPERTIES = {
            "_MainTex", "_BaseMap", "_BaseColorMap"
        };
        
        public static readonly string[] UNITY_STANDARD_TEXTURE_PROPERTIES = {
            "_MainTex", "_BaseMap", "_BaseColorMap", "_AlbedoMap"
        };
        
        public static readonly string[] VRCHAT_TEXTURE_PROPERTIES = {
            "_MainTex", "_Diffuse", "_DiffuseTexture", "_Albedo"
        };
        
        // 初期化
        public void Initialize()
        {
            InitializeComputeShaders();
        }
        
        void InitializeComputeShaders()
        {
            try
            {
                brushPainterCS = Resources.Load<ComputeShader>("BrushPainter21");
                clearShaderCS = Resources.Load<ComputeShader>("ClearShader21");
                
                if (brushPainterCS != null && SystemInfo.supportsComputeShaders)
                {
                    try
                    {
                        if (brushPainterCS.HasKernel("PaintBrush") && brushPainterCS.HasKernel("SelectionPaintOptimized"))
                        {
                            useGPUBrush = true;
                            computeShaderVerified = true;
                        }
                    }
                    catch (System.Exception)
                    {
                        useGPUBrush = false;
                        computeShaderVerified = false;
                    }
                }
                
                meshGeneratorCS = Resources.Load<ComputeShader>("MeshGenerator21");
                colorAdjustmentCS = Resources.Load<ComputeShader>("ColorAdjustment21");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"CanvasStudio: ComputeShader初期化エラー: {e.Message}");
                useGPUBrush = false;
                computeShaderVerified = false;
            }
        }
        
        public void ResetAllState()
        {
            targetObject = null;
            targetRenderer = null;
            targetMaterial = null;
            originalMaterial = null;
            originalTexture = null;
            directTexture = null;
            isDirectTextureMode = false;
            materialIndex = 0;
            
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
            
            showPaintedAreaColorAdjustment = true;
            showReferenceColorAdjustment = false;
            showCombinedColorAdjustment = false;
            
            // フラグをリセット
            isPainting = false;
            isDraggingSymmetryAxis = false;
            isDraggingUV = false;
            isDrawingStraightLine = false;
            hasLastPaintPosition = false;
            isCurrentlyPainting = false;
            preventTextureRestore = false;
            isPreviewActive = false;
            needsMaterialRestore = false;
            meshTextureDirty = true;
            lastMeshCachedObject = null;
            isSpoidMode = false;
            autoSetAfterApply = true;
            
            strokePaintedPixels.Clear();
        }
        
        public void CleanupAllResources()
        {
            RenderTexture.active = null;
            CleanupTextures();
            
            if (brushCursorTexture != null) 
            { 
                try { Object.DestroyImmediate(brushCursorTexture); } 
                catch (System.Exception) { } 
                brushCursorTexture = null; 
            }
            
            if (bucketCursorTexture != null) 
            { 
                try { Object.DestroyImmediate(bucketCursorTexture); } 
                catch (System.Exception) { } 
                bucketCursorTexture = null; 
            }
            
            if (checkerboardTexture != null)
            {
                try { Object.DestroyImmediate(checkerboardTexture); } 
                catch (System.Exception) { } 
                checkerboardTexture = null;
            }
            
            if (meshTexture != null) { meshTexture.Release(); meshTexture = null; }
        }
        
        void CleanupTextures()
        {
            RenderTexture.active = null;
            
            if (workingTexture != null) { workingTexture.Release(); workingTexture = null; }
            if (paintMask != null) { paintMask.Release(); paintMask = null; }
            if (originalTextureRT != null) { originalTextureRT.Release(); originalTextureRT = null; }
            if (paintColorTexture != null) { paintColorTexture.Release(); paintColorTexture = null; }
        }
        
        public RenderTexture CreateOptimizedRenderTexture(int width, int height, RenderTextureFormat format = RenderTextureFormat.ARGB32)
        {
            RenderTexture rt = new RenderTexture(width, height, 0, format);
            rt.enableRandomWrite = true;
            rt.Create();
            
            if (!SystemInfo.SupportsRenderTextureFormat(format))
            {
                Debug.LogWarning($"CanvasStudio: RenderTextureFormat {format} はサポートされていません");
                rt.Release();
                return null;
            }
            
            return rt;
        }
    }
}
