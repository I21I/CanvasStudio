using UnityEngine;
using UnityEditor;

namespace CanvasStudio
{
    public class CanvasStudio : EditorWindow
    {
        [SerializeField] public CanvasStudioCore core;
        [SerializeField] public UndoSystem undoSystem;
        [SerializeField] public SelectionSystem selectionSystem;
        [SerializeField] public ColorAdjustmentSystem colorAdjustmentSystem;
        [SerializeField] public PaintSystem paintSystem;
        [SerializeField] public TextureUtilities textureUtilities;
        [SerializeField] public UIDrawer uiDrawer;
        [SerializeField] public MeshDisplaySystem meshDisplaySystem;
        [SerializeField] public EditorCallbacks editorCallbacks;
        
        [MenuItem("Window/Canvas Studio")]
        public static void ShowWindow()
        {
            CanvasStudio window = GetWindow<CanvasStudio>("Canvas Studio");
            window.minSize = new Vector2(800, 600);
            window.Show();
        }
        
        void OnEnable()
        {
            // システム初期化
            if (core == null) core = new CanvasStudioCore();
            core.Initialize();
            
            if (undoSystem == null) undoSystem = new UndoSystem(this);
            if (selectionSystem == null) selectionSystem = new SelectionSystem(this);
            if (colorAdjustmentSystem == null) colorAdjustmentSystem = new ColorAdjustmentSystem(this);
            if (paintSystem == null) paintSystem = new PaintSystem(this);
            if (textureUtilities == null) textureUtilities = new TextureUtilities(this);
            if (uiDrawer == null) uiDrawer = new UIDrawer(this);
            if (meshDisplaySystem == null) meshDisplaySystem = new MeshDisplaySystem(this);
            if (editorCallbacks == null) editorCallbacks = new EditorCallbacks(this);
            
            editorCallbacks.OnEnable();
            
            // カーソルテクスチャ初期化
            try
            {
                textureUtilities.CreateBrushCursor();
                textureUtilities.CreateBucketCursor();
                textureUtilities.CreateCheckerboardTexture();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CanvasStudio: カーソルテクスチャ初期化警告: {e.Message}");
            }
        }
        
        void OnDisable()
        {
            editorCallbacks?.OnDisable();
        }
        
        void OnDestroy()
        {
            editorCallbacks?.OnDestroy();
        }
        
        void OnGUI()
        {
            if (core == null || uiDrawer == null)
            {
                EditorGUILayout.LabelField("Canvas Studio 初期化中...", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            
            try
            {
                uiDrawer.OnGUI();
            }
            catch (System.Exception e)
            {
                EditorGUILayout.HelpBox($"Canvas Studio エラー: {e.Message}", MessageType.Error);
                Debug.LogError($"CanvasStudio OnGUI エラー: {e.Message}\n{e.StackTrace}");
            }
        }
        
        void OnSelectionChange()
        {
            editorCallbacks?.OnSelectionChange();
        }
        
        void OnFocus()
        {
            editorCallbacks?.OnFocus();
        }
        
        void OnLostFocus()
        {
            editorCallbacks?.OnLostFocus();
        }
        
        void Update()
        {
            // アニメーションやリアルタイム更新が必要な場合
            if (core?.workingTexture != null)
            {
                Repaint();
            }
        }
    }
}
