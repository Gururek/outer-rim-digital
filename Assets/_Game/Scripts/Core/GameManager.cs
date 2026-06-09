// GameManager.cs — V2 per Outer Rim rulebooks
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class GameManager : NetworkBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Win Condition")][SerializeField] private int fameRequirement = 10;
        [Header("Credits")][SerializeField] private int creditsFromResting = 2000;
        [SerializeField] private int defeatedCreditPenalty = 3000;

        // Set to true before starting host for single-player testing
        private bool soloMode = false;
        public void EnableSoloMode() => soloMode = true;

        private NetworkVariable<GamePhase> currentPhase = new(GamePhase.WaitingForPlayers);
        private NetworkVariable<int> currentPlayerIndex = new(0);
        private NetworkVariable<int> turnNumber = new(1);
        private NetworkVariable<int> fameRequirementNet = new(10);

        private List<PlayerState> turnOrder = new();
        private bool planningResolved = false;
        private System.Random rng;

        public GamePhase CurrentPhase => currentPhase.Value;
        public int CurrentTurnNumber => turnNumber.Value;
        public int FameRequirement => fameRequirementNet.Value;
        public List<PlayerState> TurnOrder => turnOrder;
        public System.Action<GamePhase> OnPhaseChanged;
        public System.Action<PlayerState> OnTurnStarted;
        public System.Action<ulong, int> OnGameOver;

        private void Awake() { if (Instance == null) Instance = this; else { Destroy(gameObject); return; } rng = new System.Random(); }

        public override void OnNetworkSpawn()
        {
            currentPhase.OnValueChanged += (_, n) => OnPhaseChanged?.Invoke(n);
            if (!IsServer) return;
            fameRequirementNet.Value = fameRequirement;
            StartCoroutine(WaitAndStartGame());
        }

        private IEnumerator WaitAndStartGame()
        {
            int requiredPlayers = soloMode ? 1 : 2;
            yield return new WaitUntil(() => NetworkManager.Singleton.ConnectedClients.Count >= requiredPlayers);
            yield return new WaitForSeconds(1f);
            CollectAndOrderPlayers();
            int[] sc = { 4000, 6000, 8000, 10000 };
            for (int i = 0; i < turnOrder.Count; i++)
                turnOrder[i].SetStartingCredits(sc[Mathf.Min(i, sc.Length - 1)]);
            TransitionToPhase(GamePhase.PlanningPhase);
        }

        private void CollectAndOrderPlayers()
        {
            turnOrder.Clear();
            foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
            {
                var obj = kvp.Value.PlayerObject;
                if (obj != null && obj.TryGetComponent(out PlayerState ps))
                { ps.CurrentNodeId.Value = 0; turnOrder.Add(ps); }
                else
                {
                    var go = new GameObject($"Player_{kvp.Key}");
                    var no = go.AddComponent<NetworkObject>();
                    var newPs = go.AddComponent<PlayerState>();
                    no.SpawnWithOwnership(kvp.Key);
                    newPs.CurrentNodeId.Value = 0;
                    turnOrder.Add(newPs);
                }
            }
            Shuffle(turnOrder);
            currentPlayerIndex.Value = 0;
        }

        private void TransitionToPhase(GamePhase next)
        {
            if (!IsServer) return;
            planningResolved = false;
            currentPhase.Value = next;
            var ap = GetActivePlayer();

            if (next == GamePhase.PlanningPhase && ap != null && ap.IsDefeated.Value)
            { ap.RecoverAllDamage(); ap.StandUp(); AdvanceTurn(); return; }
            if (ap == null && next != GamePhase.CheckingWinCondition) return;

            switch (next)
            {
                case GamePhase.PlanningPhase:
                    OnTurnStarted?.Invoke(ap); NotifyPhaseClientRpc(next, ap.OwnerClientId); break;
                case GamePhase.ActionPhase:
                    NotifyPhaseClientRpc(next, ap.OwnerClientId); break;
                case GamePhase.EncounterPhase:
                    NotifyPhaseClientRpc(next, ap.OwnerClientId);
                    if (EncounterResolver.Instance != null) EncounterResolver.Instance.ResolveEncounter(ap);
                    break;
                case GamePhase.CheckingWinCondition: CheckWinCondition(); break;
            }
        }

        [ClientRpc] private void NotifyPhaseClientRpc(GamePhase p, ulong id) => OnPhaseChanged?.Invoke(p);

        // ─── PLANNING ────────────────────────────────────────────────────────
        [ServerRpc(RequireOwnership = false)]
        public void SubmitPlanningChoiceServerRpc(PlanningChoice c, ServerRpcParams p = default)
        {
            if (!ValidateActive(p.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.PlanningPhase || planningResolved) return;
            var ap = GetActivePlayer();
            if (ap.IsDefeated.Value && c != PlanningChoice.RecoverDamage) return;
            planningResolved = true;
            switch (c)
            {
                case PlanningChoice.RecoverDamage: ap.RecoverAllDamage(); if (ap.IsDefeated.Value) ap.StandUp(); AdvanceTurn(); break;
                case PlanningChoice.GainCredits: ap.AddCredits(creditsFromResting); AdvanceTurn(); break;
                case PlanningChoice.MoveShip: TransitionToPhase(GamePhase.ActionPhase); break;
            }
        }

        // ─── ACTION ──────────────────────────────────────────────────────────
        [ServerRpc(RequireOwnership = false)]
        public void ConfirmMoveServerRpc(int d, ServerRpcParams p = default)
        { if (!ValidateActive(p.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return; var ap = GetActivePlayer(); if (ShipMovement.Instance != null) ShipMovement.Instance.TryMovePlayer(ap, d); }

        [ServerRpc(RequireOwnership = false)]
        public void BuyCardServerRpc(MarketDeckType dt, int ri, ServerRpcParams p = default)
        {
            if (!ValidateActive(p.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return;
            var player = GetActivePlayer();
            var card = DeckManager.Instance?.TryPurchaseCard(player, dt, ri);
            if (card == null) return;
            // Track inventory by deck type
            switch (dt)
            {
                case MarketDeckType.Cargo:
                    if (player.CargoUsed.Value < player.CargoSlots.Value)
                        player.CargoUsed.Value++;
                    break;
                case MarketDeckType.Bounty:
                    if (player.CargoUsed.Value < player.CargoSlots.Value)
                    {
                        player.CargoUsed.Value++;
                        player.BountiesHeld.Value++;
                    }
                    break;
                case MarketDeckType.Gear:
                    if (player.GearUsed.Value < player.GearSlots.Value)
                        player.GearUsed.Value++;
                    break;
                case MarketDeckType.Mods:
                    if (player.ModUsed.Value < player.ModSlots.Value)
                        player.ModUsed.Value++;
                    break;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void CycleCardServerRpc(MarketDeckType dt, int ri, ServerRpcParams p = default)
        { if (!ValidateActive(p.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return; DeckManager.Instance?.TryCycleCard(GetActivePlayer(), dt, ri); }

        [ServerRpc(RequireOwnership = false)]
        public void DeliverCargoServerRpc(int id, ServerRpcParams p = default)
        {
            if (!ValidateActive(p.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return;
            var ap = GetActivePlayer();
            if (ap.CargoUsed.Value <= ap.BountiesHeld.Value) return; // No non-bounty cargo to deliver
            ap.CargoUsed.Value--;
            ap.AddCredits(2000);
            ap.AddFame(1);
        }

        [ServerRpc(RequireOwnership = false)]
        public void CompleteBountyServerRpc(int id, ServerRpcParams p = default)
        {
            if (!ValidateActive(p.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return;
            var ap = GetActivePlayer();
            if (ap.BountiesHeld.Value <= 0) return;
            ap.BountiesHeld.Value--;
            ap.CargoUsed.Value--;
            ap.AddCredits(5000);
            ap.AddFame(2);
        }

        [ServerRpc(RequireOwnership = false)]
        public void TradeCreditsServerRpc(ulong t, int a, ServerRpcParams p = default)
        {
            if (!ValidateActive(p.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase || a <= 0) return;
            var from = GetPlayerByClient(p.Receive.SenderClientId);
            var to = GetPlayerByClient(t);
            if (from == null || to == null || from == to || from.CurrentNodeId.Value != to.CurrentNodeId.Value) return;
            if (!from.SpendCredits(a)) return;
            to.AddCredits(a);
        }

        [ServerRpc(RequireOwnership = false)]
        public void EndActionPhaseServerRpc(ServerRpcParams p = default)
        { if (!ValidateActive(p.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return; TransitionToPhase(GamePhase.EncounterPhase); }

        public void NotifyEncounterComplete()
        { if (!IsServer || currentPhase.Value != GamePhase.EncounterPhase) return; TransitionToPhase(GamePhase.CheckingWinCondition); }

        public void AdvanceTurn()
        { if (!IsServer) return; int n = currentPlayerIndex.Value + 1; if (n >= turnOrder.Count) { n = 0; turnNumber.Value++; } currentPlayerIndex.Value = n; TransitionToPhase(GamePhase.PlanningPhase); }

        public PlayerState GetActivePlayer()
        { if (turnOrder.Count == 0) return null; int i = currentPlayerIndex.Value; return i >= 0 && i < turnOrder.Count ? turnOrder[i] : null; }

        public PlayerState GetPlayerByClient(ulong id) { foreach (var p in turnOrder) if (p.OwnerClientId == id) return p; return null; }

        private void CheckWinCondition()
        { if (!IsServer) return; foreach (var p in turnOrder) if (p.Fame.Value >= fameRequirementNet.Value) { currentPhase.Value = GamePhase.GameOver; OnGameOver?.Invoke(p.OwnerClientId, p.Fame.Value); return; } AdvanceTurn(); }

        private bool ValidateActive(ulong id) { if (!IsServer) return false; var a = GetActivePlayer(); return a != null && a.OwnerClientId == id; }
        private void Shuffle<T>(List<T> l) { int n = l.Count; while (n > 1) { n--; int k = rng.Next(n + 1); (l[k], l[n]) = (l[n], l[k]); } }
    }
}
