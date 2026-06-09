// GameUIManager.cs — V2: unified Canvas-based UI replacing DebugGameUI + GameHUD + MarketPanel
// Handles all player interaction and game state display per TDD plan.
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Collections.Generic;

namespace OuterRim
{
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public class GameUIManager : MonoBehaviour
    {
        private Canvas canvas;
        private PlayerState localPlayer;
        private bool uiCreated;

        // ─── Top bar ─────────────────────────────────────────────────────────
        private Text fameText, phaseText, turnText, creditsText;

        // ─── Left panel (action buttons) ─────────────────────────────────────
        private GameObject leftPanel;
        private Button btnMoveShip, btnRecover, btnGainCredits;   // Planning
        private Button btnMove, btnBuyBounty, btnBuyCargo, btnBuyGear, btnBuyShip, btnBuyJob, btnBuyLuxury;
        private Button btnDeliver, btnBounty, btnEndAction;       // Action
        private GameObject tradeGroup;
        private InputField tradeTargetInput, tradeAmountInput;
        private Button btnTradeSend;
        private GameObject moveGroup;
        private InputField moveNodeInput;
        private Button btnMoveConfirm;

        // ─── Right panel (stats) ─────────────────────────────────────────────
        private Text shipNameText, hdText, hullText, combatText, cargoText, crewText;
        private Text huttRepText, syndRepText, impRepText, rebelRepText;

        // ─── Market bar (bottom, action phase only) ──────────────────────────
        private Text[] marketCards;
        private Button[] marketBuyBtns;
        private Button[] marketCycleBtns;

        // ─── Misc ────────────────────────────────────────────────────────────
        private string statusMsg = "";
        private float statusTimer = 0f;

        private void Awake()
        {
            canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvas.enabled = false;

            var scaler = GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // GraphicRaycaster is auto-added by RequireComponent
            InvokeRepeating(nameof(Refresh), 0.1f, 0.25f);
        }

        private void CreateUI()
        {
            if (uiCreated) return;
            uiCreated = true;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject(); // root

            CreateTopBar(font);
            CreateLeftPanel(font);
            CreateRightPanel(font);
            CreateMarketBar(font);
        }

        // ═════════════════════════════════════════════════════════════════════
        // TOP BAR
        // ═════════════════════════════════════════════════════════════════════
        private void CreateTopBar(Font font)
        {
            var bar = Panel("TopBar", new Vector2(0, 1), new Vector2(1, 1), 42, canvas.transform);
            bar.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.08f, 0.92f);

            fameText = Label("★ 0/10", new Vector2(0.015f, 0.5f), bar.transform, font, 22, Color.yellow);
            fameText.alignment = TextAnchor.MiddleLeft;

            phaseText = Label("Waiting...", new Vector2(0.35f, 0.5f), bar.transform, font, 18, Color.cyan);
            phaseText.alignment = TextAnchor.MiddleCenter;

            turnText = Label("Turn 1", new Vector2(0.55f, 0.5f), bar.transform, font, 16, Color.gray);
            turnText.alignment = TextAnchor.MiddleCenter;

            creditsText = Label("$ 0", new Vector2(0.985f, 0.5f), bar.transform, font, 22, new Color(0.5f, 1f, 0.5f));
            creditsText.alignment = TextAnchor.MiddleRight;

            // Status message line
            var statusGo = new GameObject("StatusBar");
            statusGo.transform.SetParent(bar.transform, false);
            var statusRect = statusGo.AddComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.35f, 0f);
            statusRect.anchorMax = new Vector2(0.65f, 1f);
            statusRect.offsetMin = Vector2.zero; statusRect.offsetMax = Vector2.zero;
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEFT PANEL — Action Buttons (visible only during your turn)
        // ═════════════════════════════════════════════════════════════════════
        private void CreateLeftPanel(Font font)
        {
            leftPanel = Panel("LeftPanel", new Vector2(0, 0.06f), new Vector2(0, 0.94f), 220, canvas.transform);
            leftPanel.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.1f, 0.85f);
            leftPanel.SetActive(false);

            var title = Label("ACTIONS", new Vector2(0.1f, 0.96f), leftPanel.transform, font, 14, Color.cyan);

            float y = 0.88f;
            float spacing = 0.065f;

            // ── Planning buttons ──
            btnMoveShip = Button("MOVE SHIP", new Vector2(0.05f, y), new Vector2(0.95f, y + 0.05f), leftPanel.transform, font);
            btnMoveShip.onClick.AddListener(() => SubmitPlanning(PlanningChoice.MoveShip));
            y -= spacing + 0.02f;

            btnRecover = Button("RECOVER (full heal)", new Vector2(0.05f, y), new Vector2(0.95f, y + 0.05f), leftPanel.transform, font);
            btnRecover.onClick.AddListener(() => SubmitPlanning(PlanningChoice.RecoverDamage));
            y -= spacing + 0.02f;

            btnGainCredits = Button("GAIN +2000cr", new Vector2(0.05f, y), new Vector2(0.95f, y + 0.05f), leftPanel.transform, font);
            btnGainCredits.onClick.AddListener(() => SubmitPlanning(PlanningChoice.GainCredits));
            y -= spacing + 0.03f;

            // ── Move group (action phase) ──
            y -= 0.02f;
            moveGroup = new GameObject("MoveGroup");
            moveGroup.transform.SetParent(leftPanel.transform, false);
            var mgRt = moveGroup.AddComponent<RectTransform>();
            mgRt.anchorMin = new Vector2(0, y - 0.06f); mgRt.anchorMax = new Vector2(1, y);
            mgRt.offsetMin = Vector2.zero; mgRt.offsetMax = Vector2.zero;

            var moveLabel = Label("Move to node:", new Vector2(0.05f, 0.5f), moveGroup.transform, font, 12, Color.white);
            moveLabel.alignment = TextAnchor.MiddleLeft;
            moveNodeInput = InputField("0", new Vector2(0.4f, 0.1f), new Vector2(0.7f, 0.9f), moveGroup.transform, font);
            btnMoveConfirm = SmallButton("GO", new Vector2(0.73f, 0.1f), new Vector2(0.95f, 0.9f), moveGroup.transform, font);
            btnMoveConfirm.onClick.AddListener(ConfirmMove);
            y -= 0.08f;

            // ── Action buttons ──
            y -= 0.02f;
            btnBuyBounty = ActionButton("Buy BOUNTY", new Vector2(0.05f, y), y - spacing, 0, leftPanel.transform, font);
            btnBuyBounty.onClick.AddListener(() => BuyCard(MarketDeckType.Bounty, 0));
            y -= spacing;

            btnBuyCargo = ActionButton("Buy CARGO", new Vector2(0.05f, y), y - spacing, 0, leftPanel.transform, font);
            btnBuyCargo.onClick.AddListener(() => BuyCard(MarketDeckType.Cargo, 0));
            y -= spacing;

            btnBuyGear = ActionButton("Buy GEAR/MOD", new Vector2(0.05f, y), y - spacing, 0, leftPanel.transform, font);
            btnBuyGear.onClick.AddListener(() => BuyCard(MarketDeckType.GearAndMod, 0));
            y -= spacing;

            btnBuyJob = ActionButton("Buy JOB", new Vector2(0.05f, y), y - spacing, 0, leftPanel.transform, font);
            btnBuyJob.onClick.AddListener(() => BuyCard(MarketDeckType.Job, 0));
            y -= spacing;

            btnBuyLuxury = ActionButton("Buy LUXURY", new Vector2(0.05f, y), y - spacing, 0, leftPanel.transform, font);
            btnBuyLuxury.onClick.AddListener(() => BuyCard(MarketDeckType.Luxury, 0));
            y -= spacing;

            btnBuyShip = ActionButton("Buy SHIP", new Vector2(0.05f, y), y - spacing, 0, leftPanel.transform, font);
            btnBuyShip.onClick.AddListener(() => BuyCard(MarketDeckType.Ship, 0));
            y -= spacing;

            btnDeliver = ActionButton("DELIVER (+1★)", new Vector2(0.05f, y), y - spacing, 0, leftPanel.transform, font);
            btnDeliver.onClick.AddListener(() => Gm()?.DeliverCargoServerRpc(0));
            y -= spacing;

            btnBounty = ActionButton("BOUNTY (+2★)", new Vector2(0.05f, y), y - spacing, 0, leftPanel.transform, font);
            btnBounty.onClick.AddListener(() => Gm()?.CompleteBountyServerRpc(0));
            y -= spacing + 0.03f;

            // ── Trade ──
            y -= 0.02f;
            tradeGroup = new GameObject("TradeGroup");
            tradeGroup.transform.SetParent(leftPanel.transform, false);
            var tgRt = tradeGroup.AddComponent<RectTransform>();
            tgRt.anchorMin = new Vector2(0, y - 0.06f); tgRt.anchorMax = new Vector2(1, y);
            tgRt.offsetMin = Vector2.zero; tgRt.offsetMax = Vector2.zero;

            Label("Trade:", new Vector2(0.05f, 0.7f), tradeGroup.transform, font, 11, Color.gray).alignment = TextAnchor.MiddleLeft;
            Label("To:", new Vector2(0.05f, 0.2f), tradeGroup.transform, font, 10, Color.gray).alignment = TextAnchor.MiddleLeft;
            tradeTargetInput = InputField("0", new Vector2(0.12f, 0.05f), new Vector2(0.35f, 0.85f), tradeGroup.transform, font);
            Label("Cr:", new Vector2(0.38f, 0.2f), tradeGroup.transform, font, 10, Color.gray).alignment = TextAnchor.MiddleLeft;
            tradeAmountInput = InputField("1000", new Vector2(0.45f, 0.05f), new Vector2(0.7f, 0.85f), tradeGroup.transform, font);
            btnTradeSend = SmallButton("SEND", new Vector2(0.73f, 0.05f), new Vector2(0.95f, 0.85f), tradeGroup.transform, font);
            btnTradeSend.onClick.AddListener(SendTrade);
            y -= 0.08f;

            // ── End action ──
            y -= 0.02f;
            btnEndAction = Button("END ACTION PHASE", new Vector2(0.05f, y), new Vector2(0.95f, y + 0.06f), leftPanel.transform, font);
            btnEndAction.onClick.AddListener(() => Gm()?.EndActionPhaseServerRpc());
            var endImg = btnEndAction.GetComponent<Image>();
            endImg.color = new Color(0.8f, 0.2f, 0.2f, 0.7f);
            var ec = btnEndAction.colors;
            ec.highlightedColor = new Color(1f, 0.3f, 0.3f, 0.9f);
            btnEndAction.colors = ec;
        }

        private void SubmitPlanning(PlanningChoice c) => Gm()?.SubmitPlanningChoiceServerRpc(c);
        private void BuyCard(MarketDeckType dt, int ri) => Gm()?.BuyCardServerRpc(dt, ri);

        private void ConfirmMove()
        {
            if (int.TryParse(moveNodeInput.text, out int nodeId))
                Gm()?.ConfirmMoveServerRpc(nodeId);
        }

        private void SendTrade()
        {
            if (ulong.TryParse(tradeTargetInput.text, out ulong tid) && int.TryParse(tradeAmountInput.text, out int amt))
                Gm()?.TradeCreditsServerRpc(tid, amt);
        }

        // ═════════════════════════════════════════════════════════════════════
        // RIGHT PANEL — Ship Stats + Reputation
        // ═════════════════════════════════════════════════════════════════════
        private void CreateRightPanel(Font font)
        {
            var panel = Panel("RightPanel", new Vector2(1, 0.06f), new Vector2(1, 0.94f), 200, canvas.transform);
            panel.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.1f, 0.85f);

            var title = Label("SHIP", new Vector2(0.1f, 0.96f), panel.transform, font, 15, Color.cyan);
            title.alignment = TextAnchor.MiddleLeft;

            float y = 0.90f;
            float sp = 0.045f;

            hdText = StatLabel("HD: 2", y, panel.transform, font); y -= sp;
            hullText = StatLabel("Hull: 5/5", y, panel.transform, font); y -= sp;
            combatText = StatLabel("Combat: 2", y, panel.transform, font); y -= sp;
            cargoText = StatLabel("Cargo: 0/4", y, panel.transform, font); y -= sp;
            crewText = StatLabel("Crew: 0/2", y, panel.transform, font); y -= sp * 2;

            // Reputation
            var repTitle = Label("REPUTATION", new Vector2(0.1f, y), panel.transform, font, 12, Color.gray);
            repTitle.alignment = TextAnchor.MiddleLeft;
            y -= sp;

            huttRepText = StatLabel("Hutt: Neutral", y, panel.transform, font); y -= sp;
            syndRepText = StatLabel("Syndicate: Neutral", y, panel.transform, font); y -= sp;
            impRepText = StatLabel("Imperial: Neutral", y, panel.transform, font); y -= sp;
            rebelRepText = StatLabel("Rebel: Neutral", y, panel.transform, font);
        }

        // ═════════════════════════════════════════════════════════════════════
        // MARKET BAR — Bottom row of market cards (action phase only)
        // ═════════════════════════════════════════════════════════════════════
        private void CreateMarketBar(Font font)
        {
            // Market is shown inline in the left panel buy buttons now — we show prices there.
            // This section kept minimal: just a subtle bottom bar with market info.
            var bar = Panel("MarketBar", new Vector2(0, 0), new Vector2(1, 0), 52, canvas.transform);
            bar.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.08f, 0.9f);
        }

        // ═════════════════════════════════════════════════════════════════════
        // REFRESH LOOP
        // ═════════════════════════════════════════════════════════════════════
        private void Refresh()
        {
            // Always create UI on first frame
            if (!uiCreated) CreateUI();

            var net = NetworkManager.Singleton;
            bool connected = net != null && net.IsConnectedClient;
            canvas.enabled = true; // Always show UI

            if (!connected)
            {
                // Offline state — show host/join controls
                fameText.text = "★ 0/10";
                creditsText.text = "$ 0cr";
                phaseText.text = "OFFLINE";
                phaseText.color = Color.gray;
                turnText.text = "Not connected";

                leftPanel.SetActive(true);
                // Hide all action elements, show host/join
                btnMoveShip.gameObject.SetActive(false);
                btnRecover.gameObject.SetActive(true);
                btnRecover.GetComponentInChildren<Text>().text = "START HOST";
                btnRecover.onClick.RemoveAllListeners();
                btnRecover.onClick.AddListener(() => FindObjectOfType<NetworkBootstrapper>()?.StartSolo());
                btnRecover.interactable = true;

                btnGainCredits.gameObject.SetActive(true);
                btnGainCredits.GetComponentInChildren<Text>().text = "JOIN GAME";
                btnGainCredits.onClick.RemoveAllListeners();
                btnGainCredits.onClick.AddListener(() => FindObjectOfType<NetworkBootstrapper>()?.StartClient(""));
                btnGainCredits.interactable = true;

                // Hide action elements
                moveGroup.SetActive(false);
                btnBuyBounty.gameObject.SetActive(false);
                btnBuyCargo.gameObject.SetActive(false);
                btnBuyGear.gameObject.SetActive(false);
                btnBuyJob.gameObject.SetActive(false);
                btnBuyLuxury.gameObject.SetActive(false);
                btnBuyShip.gameObject.SetActive(false);
                btnDeliver.gameObject.SetActive(false);
                btnBounty.gameObject.SetActive(false);
                tradeGroup.SetActive(false);
                btnEndAction.gameObject.SetActive(false);

                // Clear right panel
                hdText.text = ""; hullText.text = ""; combatText.text = "";
                cargoText.text = ""; crewText.text = "";
                huttRepText.text = ""; syndRepText.text = "";
                impRepText.text = ""; rebelRepText.text = "";
                return;
            }

            // Connected state — restore button labels
            btnRecover.GetComponentInChildren<Text>().text = "RECOVER (full heal)";
            btnRecover.onClick.RemoveAllListeners();
            btnRecover.onClick.AddListener(() => SubmitPlanning(PlanningChoice.RecoverDamage));

            btnGainCredits.GetComponentInChildren<Text>().text = "GAIN +2000cr";
            btnGainCredits.onClick.RemoveAllListeners();
            btnGainCredits.onClick.AddListener(() => SubmitPlanning(PlanningChoice.GainCredits));

            // Find local player once
            if (localPlayer == null)
            {
                foreach (var ps in FindObjectsOfType<PlayerState>())
                    if (ps.IsOwner) { localPlayer = ps; break; }
            }

            var gm = Gm();
            if (gm == null || localPlayer == null) return;

            bool isMyTurn = gm.GetActivePlayer()?.OwnerClientId == net.LocalClientId;
            var phase = gm.CurrentPhase;

            // Top bar
            fameText.text = $"★ {localPlayer.Fame.Value}/{gm.FameRequirement}";
            creditsText.text = $"${localPlayer.Credits.Value:N0}cr";
            phaseText.text = FormatPhase(phase);
            phaseText.color = isMyTurn ? Color.green : Color.cyan;
            turnText.text = $"Turn {gm.CurrentTurnNumber}";

            // Right panel stats
            hdText.text = $"HD: {localPlayer.Hyperdrive.Value}";
            hullText.text = $"Hull: {localPlayer.ShipHealth.Value}/{localPlayer.MaxShipHealth.Value}";
            combatText.text = $"Combat: {localPlayer.ShipCombatValue.Value}";
            cargoText.text = $"Cargo: {localPlayer.CargoUsed.Value}/{localPlayer.CargoSlots.Value}";
            crewText.text = $"Crew: {localPlayer.CrewUsed.Value}/{localPlayer.CrewSlots.Value}";

            huttRepText.text = $"Hutt: {FormatRep(localPlayer.HuttRep.Value)}";
            syndRepText.text = $"Syndicate: {FormatRep(localPlayer.SyndicateRep.Value)}";
            impRepText.text = $"Imperial: {FormatRep(localPlayer.ImperialRep.Value)}";
            rebelRepText.text = $"Rebel: {FormatRep(localPlayer.RebelRep.Value)}";

            // Left panel — phase-specific
            UpdateLeftPanel(gm, phase, isMyTurn);

            if (isMyTurn && phase == GamePhase.ActionPhase)
            {
                UpdateMarketButtons();
            }
        }

        private void UpdateLeftPanel(GameManager gm, GamePhase phase, bool isMyTurn)
        {
            bool showPlanning = isMyTurn && phase == GamePhase.PlanningPhase;
            bool showAction = isMyTurn && phase == GamePhase.ActionPhase;

            leftPanel.SetActive(showPlanning || showAction);

            // Planning buttons
            btnMoveShip.gameObject.SetActive(showPlanning);
            btnRecover.gameObject.SetActive(showPlanning);
            btnGainCredits.gameObject.SetActive(showPlanning);

            // Action elements
            moveGroup.SetActive(showAction);
            btnBuyBounty.gameObject.SetActive(showAction);
            btnBuyCargo.gameObject.SetActive(showAction);
            btnBuyGear.gameObject.SetActive(showAction);
            btnBuyJob.gameObject.SetActive(showAction);
            btnBuyLuxury.gameObject.SetActive(showAction);
            btnBuyShip.gameObject.SetActive(showAction);
            btnDeliver.gameObject.SetActive(showAction);
            btnBounty.gameObject.SetActive(showAction);
            tradeGroup.SetActive(showAction);
            btnEndAction.gameObject.SetActive(showAction);

            // Disable buy buttons if no credits
            if (showAction)
            {
                int cr = localPlayer.Credits.Value;
                var dm = DeckManager.Instance;
                SetBuyBtn(btnBuyBounty, MarketDeckType.Bounty, cr);
                SetBuyBtn(btnBuyCargo, MarketDeckType.Cargo, cr);
                SetBuyBtn(btnBuyGear, MarketDeckType.GearAndMod, cr);
                SetBuyBtn(btnBuyJob, MarketDeckType.Job, cr);
                SetBuyBtn(btnBuyLuxury, MarketDeckType.Luxury, cr);
                SetBuyBtn(btnBuyShip, MarketDeckType.Ship, cr);
            }
        }

        private void SetBuyBtn(Button btn, MarketDeckType deck, int credits)
        {
            var dm = DeckManager.Instance;
            if (dm == null) { btn.interactable = false; return; }
            // Read synced client-side market row — NOT server-only deck internals
            int[] row = dm.GetClientMarketRow(deck);
            if (row == null || row.Length == 0) { btn.interactable = false; return; }
            btn.interactable = true; // always enable when cards are available (credit check is server-side)
            var txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                string deckName = deck.ToString();
                txt.text = $"Buy {deckName} [{row.Length} avail]";
            }
        }

        private void UpdateMarketButtons()
        {
            // Individual buy buttons in left panel already handle it.
        }

        // ═════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════════
        private GameManager Gm() => GameManager.Instance;

        private string FormatPhase(GamePhase p) => p switch
        {
            GamePhase.WaitingForPlayers => "Waiting for players...",
            GamePhase.PlanningPhase => "Planning Phase",
            GamePhase.ActionPhase => "Action Phase",
            GamePhase.EncounterPhase => "Encounter Phase",
            GamePhase.ResolvingCombat => "⚔ Combat!",
            GamePhase.CheckingWinCondition => "Checking...",
            GamePhase.GameOver => "GAME OVER",
            _ => p.ToString()
        };

        private string FormatRep(ReputationStatus r) => r switch
        {
            ReputationStatus.Positive => "▲ Friendly",
            ReputationStatus.Neutral => "■ Neutral",
            ReputationStatus.Negative => "▼ Hostile",
            _ => "?"
        };

        // ═════════════════════════════════════════════════════════════════════
        // UI FACTORY METHODS
        // ═════════════════════════════════════════════════════════════════════
        private GameObject Panel(string name, Vector2 anchor, Vector2 anchorMax, float size, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(anchor.x, anchor.y);
            if (anchor.x == anchorMax.x) // vertical panel
            {
                rt.anchorMin = new Vector2(anchor.x, 0);
                rt.anchorMax = new Vector2(anchor.x, 1);
                rt.sizeDelta = new Vector2(size, 0);
                rt.pivot = new Vector2(anchor.x > 0.5f ? 1 : 0, 0.5f);
            }
            else if (anchor.y == anchorMax.y) // horizontal bar
            {
                rt.anchorMin = new Vector2(0, anchor.y);
                rt.anchorMax = new Vector2(1, anchor.y);
                rt.sizeDelta = new Vector2(0, size);
                rt.pivot = new Vector2(0.5f, anchor.y > 0.5f ? 1 : 0);
            }
            return go;
        }

        private Text Label(string text, Vector2 anchor, Transform parent, Font font, int size, Color color)
        {
            var go = new GameObject($"Lbl_{text.Substring(0, Mathf.Min(text.Length, 20))}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var txt = go.AddComponent<Text>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(350, size + 4);
            txt.font = font; txt.fontSize = size; txt.color = color; txt.text = text;
            return txt;
        }

        private Text StatLabel(string text, float y, Transform parent, Font font)
        {
            var go = new GameObject($"Stat_{text.Substring(0, Mathf.Min(text.Length, 15))}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var txt = go.AddComponent<Text>();
            rt.anchorMin = new Vector2(0.08f, y); rt.anchorMax = new Vector2(0.08f, y);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(180, 18);
            txt.font = font; txt.fontSize = 14; txt.color = Color.white; txt.text = text;
            txt.alignment = TextAnchor.MiddleLeft;
            return txt;
        }

        private Button Button(string label, Vector2 min, Vector2 max, Transform parent, Font font)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.15f, 0.3f, 0.5f, 0.8f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.25f, 0.45f, 0.7f, 0.9f);
            colors.pressedColor = new Color(0.1f, 0.2f, 0.35f, 0.9f);
            btn.colors = colors;
            var txt = Label(label, new Vector2(0.5f, 0.5f), go.transform, font, 12, Color.white);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.rectTransform.anchorMin = Vector2.zero; txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = new Vector2(4, 2); txt.rectTransform.offsetMax = new Vector2(-4, -2);
            return btn;
        }

        private Button ActionButton(string label, Vector2 min, float yMax, int ri, Transform parent, Font font)
        {
            return Button(label, new Vector2(0.05f, yMax - 0.05f), new Vector2(0.95f, yMax), parent, font);
        }

        private Button SmallButton(string label, Vector2 min, Vector2 max, Transform parent, Font font)
        {
            var go = new GameObject($"SmBtn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.4f, 0.3f, 0.8f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var txt = Label(label, new Vector2(0.5f, 0.5f), go.transform, font, 10, Color.white);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.rectTransform.anchorMin = Vector2.zero; txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = Vector2.zero; txt.rectTransform.offsetMax = Vector2.zero;
            return btn;
        }

        private InputField InputField(string defaultText, Vector2 min, Vector2 max, Transform parent, Font font)
        {
            var go = new GameObject("Input");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);

            // InputField needs a text child
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(4, 2); textRt.offsetMax = new Vector2(-4, -2);
            var text = textGo.AddComponent<Text>();
            text.font = font; text.fontSize = 12; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.text = defaultText;

            var placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(go.transform, false);
            var phRt = placeholder.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(4, 2); phRt.offsetMax = new Vector2(-4, -2);
            var phText = placeholder.AddComponent<Text>();
            phText.font = font; phText.fontSize = 12; phText.color = Color.gray;
            phText.alignment = TextAnchor.MiddleLeft; phText.text = defaultText;

            var input = go.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = phText;
            return input;
        }
    }
}
