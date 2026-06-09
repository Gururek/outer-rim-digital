// GameManager.cs — V2: full turn cycle, 6-market buying, cargo/bounty delivery
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using Random = System.Random;

namespace OuterRim
{
    public class GameManager : NetworkBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Setup")]
        [SerializeField] private List<PlayerState> playerOrder;
        [SerializeField] private int creditsFromResting = 2000;

        private List<PlayerState> turnOrder = new();
        private bool soloMode;

        [Header("Networked State")]
        private NetworkVariable<GamePhase> currentPhase = new(GamePhase.WaitingForPlayers,
            writePerm: NetworkVariableWritePermission.Server);
        private NetworkVariable<int> currentPlayerIndex = new(0);
        private NetworkVariable<int> turnNumber = new(0);
        private NetworkVariable<int> fameRequirementNet = new(10);

        public GamePhase CurrentPhase => currentPhase.Value;
        public int FameRequirement => fameRequirementNet.Value;
        public int CurrentTurnNumber => turnNumber.Value;
        public System.Action<ulong, int> OnGameOver;

        private bool planningResolved;
        private System.Random rng = new();

        private void Awake() { if (Instance == null) Instance = this; else Destroy(gameObject); }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            if (soloMode)
            {
                // Solo: host is the only player
                var hostPlayer = FindObjectsOfType<PlayerState>().FirstOrDefault(p => p.OwnerClientId == NetworkManager.ServerClientId);
                if (hostPlayer != null) turnOrder = new List<PlayerState> { hostPlayer };
            }
            else
            {
                var allPlayers = FindObjectsOfType<PlayerState>();
                foreach (var p in allPlayers)
                    if (p.OwnerClientId != 0)
                        turnOrder.Add(p);
            }

            if (turnOrder.Count == 0) { Debug.LogWarning("[GameManager] No players connected."); return; }

            Shuffle(turnOrder);
            currentPlayerIndex.Value = 0;
            turnNumber.Value = 1;
            PatrolManager.Instance?.PlacePatrols();
            TransitionToPhase(GamePhase.PlanningPhase);
        }

        public void EnableSoloMode() => soloMode = true;

        public void TransitionToPhase(GamePhase p)
        {
            if (!IsServer) return;
            planningResolved = false;
            currentPhase.Value = p;

            switch (p)
            {
                case GamePhase.EncounterPhase:
                    var ap = GetActivePlayer();
                    if (ap == null) { AdvanceTurn(); return; }
                    var en = EncounterResolver.Instance;
                    if (en != null)
                    {
                        en.ResolveEncounter(ap);
                        return;
                    }
                    NotifyEncounterComplete();
                    break;

                case GamePhase.CheckingWinCondition:
                    CheckWinCondition();
                    break;
            }
        }

        // ─── PLANNING ────────────────────────────────────────────────────────
        [ServerRpc(RequireOwnership = false)]
        public void SubmitPlanningChoiceServerRpc(PlanningChoice c, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActive(rpcParams.Receive.SenderClientId)) return;
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
        public void ConfirmMoveServerRpc(int d, ServerRpcParams rpcParams = default)
        { if (!ValidateActive(rpcParams.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return; var ap = GetActivePlayer(); if (ShipMovement.Instance != null) ShipMovement.Instance.TryMovePlayer(ap, d); }

        [ServerRpc(RequireOwnership = false)]
        public void BuyCardServerRpc(MarketDeckType dt, int ri, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActive(rpcParams.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return;
            var player = GetActivePlayer();
            var card = DeckManager.Instance?.TryPurchaseCard(player, dt, ri);
            if (card == null) return;
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
                case MarketDeckType.GearAndMod:
                    if (card is GearCardData && player.GearUsed.Value < player.GearSlots.Value)
                        player.GearUsed.Value++;
                    else if (card is ModCardData && player.ModUsed.Value < player.ModSlots.Value)
                        player.ModUsed.Value++;
                    break;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void CycleCardServerRpc(MarketDeckType dt, int ri, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActive(rpcParams.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return;
            DeckManager.Instance?.TryCycleCard(GetActivePlayer(), dt, ri);
            if (PatrolManager.Instance != null)
                foreach (var pat in PatrolManager.Instance.GetAllPatrols())
                    PatrolManager.Instance.MovePatrol(pat);
        }

        [ServerRpc(RequireOwnership = false)]
        public void DeliverCargoServerRpc(int id, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActive(rpcParams.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return;
            var ap = GetActivePlayer();
            if (ap.CargoUsed.Value <= ap.BountiesHeld.Value) return;
            ap.CargoUsed.Value--;
            ap.AddCredits(2000);
            ap.AddFame(1);
        }

        [ServerRpc(RequireOwnership = false)]
        public void CompleteBountyServerRpc(int id, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActive(rpcParams.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return;
            var ap = GetActivePlayer();
            if (ap.BountiesHeld.Value <= 0) return;
            ap.BountiesHeld.Value--;
            ap.CargoUsed.Value--;
            ap.AddCredits(5000);
            ap.AddFame(2);
        }

        [ServerRpc(RequireOwnership = false)]
        public void TradeCreditsServerRpc(ulong t, int a, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActive(rpcParams.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase || a <= 0) return;
            var from = GetPlayerByClient(rpcParams.Receive.SenderClientId);
            var to = GetPlayerByClient(t);
            if (from == null || to == null || from == to || from.CurrentNodeId.Value != to.CurrentNodeId.Value) return;
            if (!from.SpendCredits(a)) return;
            to.AddCredits(a);
        }

        [ServerRpc(RequireOwnership = false)]
        public void EndActionPhaseServerRpc(ServerRpcParams rpcParams = default)
        { if (!ValidateActive(rpcParams.Receive.SenderClientId) || currentPhase.Value != GamePhase.ActionPhase) return; TransitionToPhase(GamePhase.EncounterPhase); }

        public void NotifyEncounterComplete()
        { if (!IsServer || currentPhase.Value != GamePhase.EncounterPhase) return; TransitionToPhase(GamePhase.CheckingWinCondition); }

        public void AdvanceTurn()
        { if (!IsServer) return; int n = currentPlayerIndex.Value + 1; if (n >= turnOrder.Count) { n = 0; turnNumber.Value++; } currentPlayerIndex.Value = n; TransitionToPhase(GamePhase.PlanningPhase); }

        public PlayerState GetActivePlayer()
        { if (turnOrder.Count == 0) return null; int i = currentPlayerIndex.Value; return i >= 0 && i < turnOrder.Count ? turnOrder[i] : null; }

        public PlayerState GetPlayerByClient(ulong id) { foreach (var pl in turnOrder) if (pl.OwnerClientId == id) return pl; return null; }

        private void CheckWinCondition()
        { if (!IsServer) return; foreach (var pl in turnOrder) if (pl.Fame.Value >= fameRequirementNet.Value) { currentPhase.Value = GamePhase.GameOver; OnGameOver?.Invoke(pl.OwnerClientId, pl.Fame.Value); return; } AdvanceTurn(); }

        private bool ValidateActive(ulong id) { if (!IsServer) return false; var a = GetActivePlayer(); return a != null && a.OwnerClientId == id; }
        private void Shuffle<T>(List<T> l) { int n = l.Count; while (n > 1) { n--; int k = rng.Next(n + 1); (l[k], l[n]) = (l[n], l[k]); } }
    }
}
