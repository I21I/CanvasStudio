using UnityEngine;
using UnityEditor;

namespace CanvasStudio
{
    public partial class CanvasStudio : EditorWindow
    {
        [MenuItem("21CSX/Canvas Studio")]
        public static void ShowWindow()
        {
            CanvasStudio window = GetWindow<CanvasStudio>("Canvas Studio");
            window.minSize = new Vector2(900, 700);
            window.ResetAllState();
            window.Initialize();
            window.Focus();
        }

        void OnGUI()
        {
            // リアルタイム更新処理
            ProcessRealTimeUpdates();
            
            EditorGUILayout.BeginHorizontal();
            
            DrawLeftPanel();
            
            EditorGUILayout.BeginVertical(GUILayout.Width(2));
            GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndVertical();
            
            DrawRightPanel();
            
            EditorGUILayout.EndHorizontal();
            
            // ズーム情報と倍率リセットボタンを右上に直接描画
            DrawZoomInfoOverlay();
            
            HandleKeyboardInput();
        }

        void Initialize()
        {
            InitializeComputeShaders();
            CreateCheckerboardTexture();
            
            try
            {
                CreateBrushCursor();
                CreateBucketCursor();
            }
            catch (System.Exception)
            {
                Debug.LogError("Canvas Studio: カーソルテクスチャ初期化エラー");
            }
        }

        void HandleKeyboardInput()
        {
            Event e = Event.current;
            
            if (e.type == EventType.KeyDown)
            {
                bool isToolFocused = EditorWindow.focusedWindow == this;
                
                if (e.control && e.keyCode == KeyCode.Z)
                {
                    if (isToolFocused && workingTexture != null)
                    {
                        PerformUnifiedUndo();
                        e.Use();
                    }
                }
                else if (e.control && e.keyCode == KeyCode.Y)
                {
                    if (isToolFocused && workingTexture != null)
                    {
                        PerformUnifiedRedo();
                        e.Use();
                    }
                }
            }
        }

        void DrawZoomInfoOverlay()
        {
            // ウィンドウの右上に直接描画（レイアウト制約を無視）
            float windowWidth = position.width;
            float rightMargin = 10f;
            float topMargin = 2f;
            
            // 倍率リセットボタン（右端）
            Rect buttonRect = new Rect(windowWidth - 80 - rightMargin, topMargin, 80, 20);
            if (GUI.Button(buttonRect, "倍率リセット"))
            {
                uvZoom = 1f;
                uvPanOffset = Vector2.zero;
                meshTextureDirty = true;
            }
            
            // ズーム情報（ボタンの左）
            Rect zoomRect = new Rect(windowWidth - 160 - rightMargin, topMargin + 2, 75, 18);
            GUI.Label(zoomRect, $"ズーム: {uvZoom:F2}x", EditorStyles.miniLabel);
        }
    }
}
