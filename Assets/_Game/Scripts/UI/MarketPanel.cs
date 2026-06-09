// MarketPanel.cs — V2: clickable market row with buy/cycle functionality
// Displays the top card of each deck at the bottom of the screen.
// Shows only during Action Phase when it's your turn.
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

namespace OuterRim
{
    public class MarketPanel : MonoBehaviour
    {
        private Canvas canvas;
        private PlayerState localPlayer;

        // Per-deck UI elements
        private struct DeckColumn
        {
            public MarketDeckType DeckType;
            public Text HeaderText;
            public Text CardNameText;
            public Text CostText;
            public Button BuyButton;
            public Button CycleButton;
        }
        private DeckColumn[] columns;

        private void Start()
        {
            CreatePanel();
            InvokeRepeating(nameof(Refresh), 0.5f, 0.5f);
        }

        private void CreatePanel()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 91;
            gameObject.AddComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            gameObject.AddComponent<GraphicRaycaster>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Root panel — bottom bar
            var root = CreateUIElement("MarketRoot", canvas.transform,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 90));
            var rootImg = root.AddComponent<Image>();
            rootImg.color = new Color(0.03f, 0.03f, 0.1f, 0.92f);

            // Title
            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(root.transform, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 0.88f);
            titleRt.anchorMax = new Vector2(0.5f, 0.88f);
            titleRt.sizeDelta = new Vector2(200, 20);
            var titleTxt = titleGo.AddComponent<Text>();
            titleTxt.font = font; titleTxt.fontSize = 12; titleTxt.color = Color.cyan;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.text = "MARKET";

            // Status
            var statusGo = new GameObject("Status");
            statusGo.transform.SetParent(root.transform, false);
            var statusRt = statusGo.AddComponent<RectTransform>();
            statusRt.anchorMin = new Vector2(0.02f, 0.12f);
            statusRt.anchorMax = new Vector2(0.5f, 0.12f);
            statusRt.sizeDelta = new Vector2(400, 18);
            var statusTxt = statusGo.AddComponent<Text>();
            statusTxt.font = font; statusTxt.fontSize = 11; statusTxt.color = Color.yellow;
            statusTxt.alignment = TextAnchor.MiddleLeft;

            // 6 deck columns
            var decks = new[]
            {
                (MarketDeckType.Bounty, "BOUNTY", new Color(1f, 0.5f, 0f)),
                (MarketDeckType.Cargo, "CARGO", new Color(0.5f, 0.5f, 1f)),
                (MarketDeckType.GearAndMod, "GEAR/MOD", new Color(0.5f, 1f, 0.5f)),
                (MarketDeckType.Job, "JOBS", new Color(1f, 1f, 0.3f)),
                (MarketDeckType.Luxury, "LUXURY", new Color(1f, 0.3f, 1f)),
                (MarketDeckType.Ship, "SHIPS", new Color(0.3f, 1f, 1f)),
            };

            columns = new DeckColumn[decks.Length];
            for (int i = 0; i < decks.Length; i++)
            {
                float x = 0.08f + i * 0.15f;
                var (type, label, color) = decks[i];

                // Column background (subtle)
                var colBg = CreateUIElement($"Col_{label}", root.transform,
                    new Vector2(x - 0.05f, 0.05f), new Vector2(x + 0.05f, 0.95f),
                    Vector2.zero);
                var colImg = colBg.AddComponent<Image>();
                colImg.color = new Color(color.r, color.g, color.b, 0.08f);

                // Header
                var header = CreateLabel(label, root.transform, new Vector2(x, 0.78f), font, 10, color);
                header.alignment = TextAnchor.MiddleCenter;

                // Card name (on clickable button background)
                var cardBtnGo = CreateUIElement($"BuyBtn_{label}", root.transform,
                    new Vector2(x - 0.04f, 0.35f), new Vector2(x + 0.04f, 0.72f),
                    Vector2.zero);
                var cardImg = cardBtnGo.AddComponent<Image>();
                cardImg.color = new Color(0.15f, 0.15f, 0.25f, 0.9f);

                var nameTxt = CreateLabel("?", root.transform, new Vector2(x, 0.58f), font, 12, Color.white);
                nameTxt.alignment = TextAnchor.MiddleCenter;

                var costTxt = CreateLabel("", root.transform, new Vector2(x, 0.42f), font, 10, Color.yellow);
                costTxt.alignment = TextAnchor.MiddleCenter;

                // Buy button (invisible, overlaid on card area)
                var buyBtn = cardBtnGo.AddComponent<Button>();
                buyBtn.targetGraphic = cardImg;
                var buyColors = buyBtn.colors;
                buyColors.highlightedColor = new Color(0.25f, 0.25f, 0.4f, 0.9f);
                buyBtn.colors = buyColors;
                var dt = type; // capture for closure
                buyBtn.onClick.AddListener(() => OnBuyClicked(dt, 0));

                // Cycle button (small)
                var cycleBtnGo = CreateUIElement($"CycleBtn_{label}", root.transform,
                    new Vector2(x - 0.03f, 0.22f), new Vector2(x + 0.03f, 0.33f),
                    Vector2.zero);
                var cycleImg = cycleBtnGo.AddComponent<Image>();
                cycleImg.color = new Color(0.2f, 0.2f, 0.15f, 0.8f);

                var cycleTxt = CreateLabel("↻ 200cr", root.transform, new Vector2(x, 0.28f), font, 9, Color.gray);
                cycleTxt.alignment = TextAnchor.MiddleCenter;

                var cycleBtn = cycleBtnGo.AddComponent<Button>();
                cycleBtn.targetGraphic = cycleImg;
                var cycleColors = cycleBtn.colors;
                cycleColors.highlightedColor = new Color(0.3f, 0.3f, 0.2f, 0.8f);
                cycleBtn.colors = cycleColors;
                var ct = type; // capture for closure
                cycleBtn.onClick.AddListener(() => OnCycleClicked(ct, 0));

                columns[i] = new DeckColumn
                {
                    DeckType = type,
                    HeaderText = header,
                    CardNameText = nameTxt,
                    CostText = costTxt,
                    BuyButton = buyBtn,
                    CycleButton = cycleBtn,
                };
            }

            // Store status text reference
            var stRef = statusTxt;
        }

        private void OnBuyClicked(MarketDeckType deckType, int rowIndex)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            gm.BuyCardServerRpc(deckType, rowIndex);
            Debug.Log($"[MarketPanel] Buy {deckType}[{rowIndex}]");
        }

        private void OnCycleClicked(MarketDeckType deckType, int rowIndex)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            gm.CycleCardServerRpc(deckType, rowIndex);
            Debug.Log($"[MarketPanel] Cycle {deckType}[{rowIndex}]");
        }

        private void Refresh()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) return;

            if (localPlayer == null)
            {
                foreach (var ps in FindObjectsOfType<PlayerState>())
                    if (ps.IsOwner) { localPlayer = ps; break; }
            }

            var dm = DeckManager.Instance;
            var gm = GameManager.Instance;
            if (dm == null || gm == null || columns == null) return;

            bool isActionPhase = gm.CurrentPhase == GamePhase.ActionPhase;
            bool isMyTurn = gm.GetActivePlayer()?.OwnerClientId == NetworkManager.Singleton.LocalClientId;

            gameObject.SetActive(isActionPhase && isMyTurn);

            if (!isActionPhase || !isMyTurn) return;

            int playerCredits = localPlayer != null ? localPlayer.Credits.Value : 0;

            foreach (var col in columns)
            {
                var deck = dm.GetDeck(col.DeckType);
                if (deck == null || deck.MarketRow.Count == 0)
                {
                    col.CardNameText.text = "Empty";
                    col.CostText.text = "";
                    col.BuyButton.interactable = false;
                    col.CycleButton.interactable = false;
                    continue;
                }

                var topCard = deck.MarketRow[0];
                string cardName = topCard != null ? (topCard.CardName ?? "Card #" + topCard.CardId) : "?";
                int cost = topCard != null ? topCard.BuyCost : 999999;

                col.CardNameText.text = cardName;
                col.CostText.text = $"${cost:N0}";
                col.BuyButton.interactable = playerCredits >= cost;
                col.CycleButton.interactable = playerCredits >= 200;
            }
        }

        // ─── UI Helpers ───────────────────────────────────────────────────────

        private GameObject CreateUIElement(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        private Text CreateLabel(string text, Transform parent, Vector2 anchor, Font font, int size, Color color)
        {
            var go = new GameObject($"Lbl_{text}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var txt = go.AddComponent<Text>();

            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(180, size + 4);

            txt.font = font;
            txt.fontSize = size;
            txt.color = color;
            txt.text = text;

            return txt;
        }
    }
}
