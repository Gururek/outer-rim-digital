// GameUIManager.cs — UI Toolkit (UIDocument) rewrite of the unified in-game UI.
// Drives GameUI.uxml / GameUI.uss. Preserves the legacy polling Refresh model and bindings.
using System;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

namespace OuterRim
{
    [RequireComponent(typeof(UIDocument))]
    public class GameUIManager : MonoBehaviour
    {
        private VisualElement root;
        private PlayerState localPlayer;
        private bool wired;

        // Dual-purpose button actions (offline host/join vs connected planning)
        private Action recoverAction;
        private Action gainAction;

        // ─── Top bar ─────────────────────────────────────────────────────────
        private Label fameText, phaseText, turnText, creditsText;

        // ─── Left panel ──────────────────────────────────────────────────────
        private VisualElement leftPanel;
        private Button btnMoveShip, btnRecover, btnGainCredits;
        private VisualElement moveGroup;
        private TextField moveNodeInput;
        private Button btnMoveConfirm;
        private Button btnBuyBounty, btnBuyCargo, btnBuyGear, btnBuyJob, btnBuyLuxury, btnBuyShip;
        private Button btnDeliver, btnBounty;
        private VisualElement tradeGroup;
        private TextField tradeTargetInput, tradeAmountInput;
        private Button btnTradeSend;
        private Button btnEndAction;

        // ─── Right panel ─────────────────────────────────────────────────────
        private VisualElement rightPanel;
        private Label hdText, hullText, combatText, cargoText, crewText;
        private Label huttRepText, syndRepText, impRepText, rebelRepText;

        private static readonly Color PhaseMyTurn = new Color(0.4f, 1f, 0.6f);   // green
        private static readonly Color PhaseOther = new Color(0.2f, 0.8f, 1f);    // cyan

        private void Awake()
        {
            // rootVisualElement may be null in Awake; wiring is done lazily in
            // OnEnable / first Refresh once the visual tree is available.
            InvokeRepeating(nameof(Refresh), 0.1f, 0.25f);
        }

        private void OnEnable()
        {
            TryWire();
        }

        private void TryWire()
        {
            if (wired) return;
            var doc = GetComponent<UIDocument>();
            root = doc != null ? doc.rootVisualElement : null;
            if (root == null) return;

            // ── Query named elements ──
            fameText = root.Q<Label>("fame-text");
            phaseText = root.Q<Label>("phase-text");
            turnText = root.Q<Label>("turn-text");
            creditsText = root.Q<Label>("credits-text");

            leftPanel = root.Q<VisualElement>("left-panel");
            btnMoveShip = root.Q<Button>("btn-move-ship");
            btnRecover = root.Q<Button>("btn-recover");
            btnGainCredits = root.Q<Button>("btn-gain-credits");

            moveGroup = root.Q<VisualElement>("move-group");
            moveNodeInput = root.Q<TextField>("move-node-input");
            btnMoveConfirm = root.Q<Button>("btn-move-confirm");

            btnBuyBounty = root.Q<Button>("btn-buy-bounty");
            btnBuyCargo = root.Q<Button>("btn-buy-cargo");
            btnBuyGear = root.Q<Button>("btn-buy-gear");
            btnBuyJob = root.Q<Button>("btn-buy-job");
            btnBuyLuxury = root.Q<Button>("btn-buy-luxury");
            btnBuyShip = root.Q<Button>("btn-buy-ship");
            btnDeliver = root.Q<Button>("btn-deliver");
            btnBounty = root.Q<Button>("btn-bounty");

            tradeGroup = root.Q<VisualElement>("trade-group");
            tradeTargetInput = root.Q<TextField>("trade-target-input");
            tradeAmountInput = root.Q<TextField>("trade-amount-input");
            btnTradeSend = root.Q<Button>("btn-trade-send");

            btnEndAction = root.Q<Button>("btn-end-action");

            rightPanel = root.Q<VisualElement>("right-panel");
            hdText = root.Q<Label>("hd-text");
            hullText = root.Q<Label>("hull-text");
            combatText = root.Q<Label>("combat-text");
            cargoText = root.Q<Label>("cargo-text");
            crewText = root.Q<Label>("crew-text");
            huttRepText = root.Q<Label>("hutt-rep-text");
            syndRepText = root.Q<Label>("synd-rep-text");
            impRepText = root.Q<Label>("imp-rep-text");
            rebelRepText = root.Q<Label>("rebel-rep-text");

            // If the visual tree has not loaded yet (no PanelSettings/UXML assigned),
            // the queries return null. Bail without wiring so we retry next Refresh
            // instead of throwing NullReferenceExceptions.
            if (fameText == null || btnMoveShip == null || btnEndAction == null || rightPanel == null)
            {
                root = null;
                return;
            }

            // ── Register button callbacks ──
            btnMoveShip.clicked += () => SubmitPlanning(PlanningChoice.MoveShip);
            btnRecover.clicked += () => recoverAction?.Invoke();
            btnGainCredits.clicked += () => gainAction?.Invoke();
            btnMoveConfirm.clicked += ConfirmMove;
            btnBuyBounty.clicked += () => BuyCard(MarketDeckType.Bounty, 0);
            btnBuyCargo.clicked += () => BuyCard(MarketDeckType.Cargo, 0);
            btnBuyGear.clicked += () => BuyCard(MarketDeckType.GearAndMod, 0);
            btnBuyJob.clicked += () => BuyCard(MarketDeckType.Job, 0);
            btnBuyLuxury.clicked += () => BuyCard(MarketDeckType.Luxury, 0);
            btnBuyShip.clicked += () => BuyCard(MarketDeckType.Ship, 0);
            btnDeliver.clicked += () => Gm()?.DeliverCargoServerRpc(0);
            btnBounty.clicked += () => Gm()?.CompleteBountyServerRpc(0);
            btnTradeSend.clicked += SendTrade;
            btnEndAction.clicked += () => Gm()?.EndActionPhaseServerRpc();

            wired = true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // REFRESH LOOP
        // ═════════════════════════════════════════════════════════════════════
        private void Refresh()
        {
            if (!wired) TryWire();
            if (!wired) return;

            var net = NetworkManager.Singleton;
            bool connected = net != null && net.IsConnectedClient;

            if (!connected)
            {
                // Offline state — show host/join controls
                fameText.text = "★ 0/10";
                creditsText.text = "$ 0cr";
                phaseText.text = "OFFLINE";
                phaseText.style.color = PhaseOther;
                turnText.text = "Not connected";

                Show(leftPanel, true);
                Show(btnMoveShip, false);

                Show(btnRecover, true);
                btnRecover.text = "START HOST";
                btnRecover.SetEnabled(true);
                recoverAction = () => FindObjectOfType<NetworkBootstrapper>()?.StartSolo();

                Show(btnGainCredits, true);
                btnGainCredits.text = "JOIN GAME";
                btnGainCredits.SetEnabled(true);
                gainAction = () => FindObjectOfType<NetworkBootstrapper>()?.StartClient("");

                Show(moveGroup, false);
                Show(btnBuyBounty, false);
                Show(btnBuyCargo, false);
                Show(btnBuyGear, false);
                Show(btnBuyJob, false);
                Show(btnBuyLuxury, false);
                Show(btnBuyShip, false);
                Show(btnDeliver, false);
                Show(btnBounty, false);
                Show(tradeGroup, false);
                Show(btnEndAction, false);

                hdText.text = ""; hullText.text = ""; combatText.text = "";
                cargoText.text = ""; crewText.text = "";
                huttRepText.text = ""; syndRepText.text = "";
                impRepText.text = ""; rebelRepText.text = "";
                return;
            }

            // Connected state — restore dual-purpose button labels/actions
            btnRecover.text = "RECOVER (full heal)";
            recoverAction = () => SubmitPlanning(PlanningChoice.RecoverDamage);
            btnGainCredits.text = "GAIN +2000cr";
            gainAction = () => SubmitPlanning(PlanningChoice.GainCredits);

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
            phaseText.style.color = isMyTurn ? PhaseMyTurn : PhaseOther;
            turnText.text = $"Turn {gm.CurrentTurnNumber}";

            // Right panel stats
            hdText.text = $"HD: {localPlayer.Hyperdrive.Value}";
            hullText.text = $"Hull: {localPlayer.ShipHealth.Value}/{localPlayer.MaxShipHealth.Value}";
            combatText.text = $"Combat: {localPlayer.ShipCombatValue.Value}";
            cargoText.text = $"Cargo: {localPlayer.CargoUsed.Value}/{localPlayer.CargoSlots.Value}";
            crewText.text = $"Crew: {localPlayer.CrewUsed.Value}/{localPlayer.CrewSlots.Value}";
            if (localPlayer.BountiesHeld.Value > 0)
                cargoText.text += $" (+{localPlayer.BountiesHeld.Value}★ bounty)";

            huttRepText.text = $"Hutt: {FormatRep(localPlayer.HuttRep.Value)}";
            syndRepText.text = $"Syndicate: {FormatRep(localPlayer.SyndicateRep.Value)}";
            impRepText.text = $"Imperial: {FormatRep(localPlayer.ImperialRep.Value)}";
            rebelRepText.text = $"Rebel: {FormatRep(localPlayer.RebelRep.Value)}";

            // Left panel — phase-specific visibility
            bool showPlanning = isMyTurn && phase == GamePhase.PlanningPhase;
            bool showAction = isMyTurn && phase == GamePhase.ActionPhase;

            Show(leftPanel, showPlanning || showAction);

            Show(btnMoveShip, showPlanning);
            Show(btnRecover, showPlanning);
            Show(btnGainCredits, showPlanning);

            Show(moveGroup, showAction);
            Show(btnBuyBounty, showAction);
            Show(btnBuyCargo, showAction);
            Show(btnBuyGear, showAction);
            Show(btnBuyJob, showAction);
            Show(btnBuyLuxury, showAction);
            Show(btnBuyShip, showAction);
            Show(btnDeliver, showAction);
            Show(btnBounty, showAction);
            Show(tradeGroup, showAction);
            Show(btnEndAction, showAction);

            if (showAction)
            {
                SetBuyBtn(btnBuyBounty, MarketDeckType.Bounty);
                SetBuyBtn(btnBuyCargo, MarketDeckType.Cargo);
                SetBuyBtn(btnBuyGear, MarketDeckType.GearAndMod);
                SetBuyBtn(btnBuyJob, MarketDeckType.Job);
                SetBuyBtn(btnBuyLuxury, MarketDeckType.Luxury);
                SetBuyBtn(btnBuyShip, MarketDeckType.Ship);
            }
        }

        private void SetBuyBtn(Button btn, MarketDeckType deck)
        {
            var dm = DeckManager.Instance;
            if (dm == null) { btn.SetEnabled(false); return; }
            // Read synced client-side market row — NOT server-only deck internals
            int[] row = dm.GetClientMarketRow(deck);
            if (row == null || row.Length == 0) { btn.SetEnabled(false); return; }
            btn.SetEnabled(true); // always enable when cards available (credit check is server-side)
            string deckName = deck.ToString();
            btn.text = $"Buy {deckName} [{row.Length} avail]";
        }

        // ═════════════════════════════════════════════════════════════════════
        // ACTIONS
        // ═════════════════════════════════════════════════════════════════════
        private GameManager Gm() => GameManager.Instance;
        private void SubmitPlanning(PlanningChoice c) => Gm()?.SubmitPlanningChoiceServerRpc(c);
        private void BuyCard(MarketDeckType dt, int ri) => Gm()?.BuyCardServerRpc(dt, ri);

        private void ConfirmMove()
        {
            if (int.TryParse(moveNodeInput.value, out int nodeId))
                Gm()?.ConfirmMoveServerRpc(nodeId);
        }

        private void SendTrade()
        {
            if (ulong.TryParse(tradeTargetInput.value, out ulong tid) && int.TryParse(tradeAmountInput.value, out int amt))
                Gm()?.TradeCreditsServerRpc(tid, amt);
        }

        // ═════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════════
        private static void Show(VisualElement e, bool visible)
        {
            if (e != null) e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

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
    }
}
