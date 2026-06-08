// CockpitOverlay.cs — Phase 2: basic cockpit frame UI overlay
using UnityEngine;
using UnityEngine.UI;

namespace OuterRim
{
    public class CockpitOverlay : MonoBehaviour
    {
        [Header("Frame Settings")]
        [SerializeField] private Color frameColor = new(0.1f, 0.1f, 0.15f, 0.85f);
        [SerializeField] private float frameThickness = 60f;
        [SerializeField] private float cornerRadius = 20f;

        private Canvas canvas;
        private GameObject[] framePanels = new GameObject[4]; // top, bottom, left, right
        private Text infoText;

        private void Awake()
        {
            CreateCanvas();
            CreateFrame();
            CreateInfoPanel();
        }

        private void CreateCanvas()
        {
            var canvasGo = new GameObject("CockpitCanvas");
            canvasGo.transform.SetParent(transform);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        private void CreateFrame()
        {
            // Top bar
            framePanels[0] = CreatePanel("TopBar", new Vector2(0, 1), new Vector2(0, frameThickness));
            // Bottom bar
            framePanels[1] = CreatePanel("BottomBar", new Vector2(0, 0), new Vector2(0, frameThickness));
            // Left bar
            framePanels[2] = CreatePanel("LeftBar", new Vector2(0, 0.5f), new Vector2(frameThickness, 0));
            // Right bar
            framePanels[3] = CreatePanel("RightBar", new Vector2(1, 0.5f), new Vector2(frameThickness, 0));

            // Corner accents
            CreateCorner(new Vector2(0, 1), "TopLeft");
            CreateCorner(new Vector2(1, 1), "TopRight");
            CreateCorner(new Vector2(0, 0), "BottomLeft");
            CreateCorner(new Vector2(1, 0), "BottomRight");
        }

        private GameObject CreatePanel(string name, Vector2 anchor, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = frameColor;

            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;

            if (sizeDelta.x > 0)
                rt.sizeDelta = new Vector2(sizeDelta.x, Screen.height);
            else
                rt.sizeDelta = new Vector2(Screen.width, sizeDelta.y);

            return go;
        }

        private void CreateCorner(Vector2 anchor, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = new Color(0.3f, 0.5f, 0.8f, 0.6f);

            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(cornerRadius, cornerRadius);

            // Position offset from corner
            float offsetX = anchor.x < 0.5f ? 10 : -10;
            float offsetY = anchor.y < 0.5f ? 10 : -10;
            rt.anchoredPosition = new Vector2(offsetX, offsetY);
        }

        private void CreateInfoPanel()
        {
            var go = new GameObject("InfoText");
            go.transform.SetParent(canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            infoText = go.AddComponent<Text>();
            infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            infoText.fontSize = 14;
            infoText.color = new Color(0.3f, 0.7f, 1f);
            infoText.alignment = TextAnchor.UpperLeft;

            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(frameThickness + 10, -frameThickness - 10);
            rt.sizeDelta = new Vector2(300, 100);
        }

        public void SetInfoText(string text)
        {
            if (infoText != null) infoText.text = text;
        }
    }
}
