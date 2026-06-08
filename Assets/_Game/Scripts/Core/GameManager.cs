// GameManager.cs — Central game state machine (Outer Rim rules)
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class GameManager : NetworkBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Win Condition")]
        [SerializeField] private int fameToWin = 10;

        [Header("Planning Phase")]
        [SerializeField] private int creditsForResting = 2000;

        [Header("Player Config")]
        [SerializeField] private int minPlayersToStart = 2;

        private NetworkVariable<GamePhase> currentPhase = new(GamePhase.WaitingForPlayers);
        private NetworkVariable<int> currentPlayerIndex = new(0);
        private NetworkVariable<int> turnNumber = new(1);

        private List<PlayerState> turnOrder = new();
        private bool planningChoiceMade = false;
        private System.Random rng;

        public GamePhase CurrentPhase => currentPhase.Value;
        public int CurrentTurnNumber => turnNumber.Value;
        public List<PlayerState> TurnOrder => turnOrder;

        public System.Action<GamePhase> OnPhaseChanged;
        public System.Action<PlayerState> OnTurnStarted;
        public System.Action<ulong> OnGameOver;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
            rng = new System.Random();
        }

        public override void OnNetworkSpawn()
        {
            currentPhase.OnValueChanged += HandlePhaseChanged;
            currentPlayerIndex.OnValueChanged += HandleActivePlayerChanged;
            if (!IsServer) return;
            StartCoroutine(WaitForPlayersCoroutine());
        }

        public override void OnNetworkDespawn()
        {
            currentPhase.OnValueChanged -= HandlePhaseChanged;
            currentPlayerIndex.OnValueChanged -= HandleActivePlayerChanged;
        }

        private IEnumerator WaitForPlayersCoroutine()
        {
            yield return new WaitUntil(() => NetworkManager.Singleton.ConnectedClients.Count >= minPlayersToStart);
            yield return new WaitForSeconds(0.5f);
            CollectAndOrderPlayers();
            TransitionToPhase(GamePhase.PlanningPhase);
        }

        private void CollectAndOrderPlayers()
        {
            turnOrder.Clear();
            foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
            {
                var playerObj = kvp.Value.PlayerObject;
                if (playerObj != null && playerObj.TryGetComponent(out PlayerState ps))
                { ps.CurrentNodeId.Value = 0; turnOrder.Add(ps); }
                else
                {
                    var go = new GameObject($"Player_{kvp.Key}");
                    var netObj = go.AddComponent<NetworkObject>();
                    var newPs = go.AddComponent<PlayerState>();
                    netObj.SpawnWithOwnership(kvp.Key);
                    newPs.CurrentNodeId.Value = 0;
                    turnOrder.Add(newPs);
                }
            }
            Shuffle(turnOrder);
            currentPlayerIndex.Value = 0;
        }

        private void TransitionToPhase(GamePhase newPhase)
        {
            if (!IsServer) return;
            planningChoiceMade = false;
            currentPhase.Value = newPhase;

            // Defeated players auto-heal
            if (newPhase == GamePhase.PlanningPhase)
            {
                var ap = GetActivePlayer();
                if (ap != null && ap.IsDefeated.Value)
                {
                    ap.Health.Value = ap.MaxHealth.Value;
                    ap.ShipHealth.Value = ap.MaxShipHealth.Value;
                    ap.RecoverFromDefeat();
                    AdvanceTurn();
                    return;
                }
            }

            var activePlayer = GetActivePlayer();
            if (activePlayer == null && newPhase != GamePhase.CheckingWinCondition) return;

            switch (newPhase)
            {
                case GamePhase.PlanningPhase:
                    OnTurnStarted?.Invoke(activePlayer);
                    BeginPlanningPhaseClientRpc(activePlayer.OwnerClientId);
                    break;
                case GamePhase.ActionPhase:
                    BeginActionPhaseClientRpc(activePlayer.OwnerClientId);
                    break;
                case GamePhase.EncounterPhase:
                    BeginEncounterPhaseClientRpc(activePlayer.OwnerClientId);
                    if (EncounterResolver.Instance != null)
                        EncounterResolver.Instance.ResolveEncounter(activePlayer);
                    break;
                case GamePhase.CheckingWinCondition:
                    CheckWinCondition();
                    break;
            }
        }

        [ClientRpc] private void BeginPlanningPhaseClientRpc(ulong id) => OnPhaseChanged?.Invoke(GamePhase.PlanningPhase);
        [ClientRpc] private void BeginActionPhaseClientRpc(ulong id) => OnPhaseChanged?.Invoke(GamePhase.ActionPhase);
        [ClientRpc] private void BeginEncounterPhaseClientRpc(ulong id) => OnPhaseChanged?.Invoke(GamePhase.EncounterPhase);

        // ═════════════════════════════════════════════════════════════
        // PLANNING PHASE
        // ═════════════════════════════════════════════════════════════

        [ServerRpc(RequireOwnership = false)]
        public void SubmitPlanningChoiceServerRpc(PlanningChoice choice, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.PlanningPhase) return;
            if (planningChoiceMade) return;
            planningChoiceMade = true;
            var player = GetActivePlayer();
            switch (choice)
            {
                case PlanningChoice.MoveShip: TransitionToPhase(GamePhase.ActionPhase); break;
                case PlanningChoice.HealDamage:
                    player.Health.Value = player.MaxHealth.Value;
                    player.ShipHealth.Value = player.MaxShipHealth.Value;
                    AdvanceTurn(); break;
                case PlanningChoice.CollectCredits:
                    player.Credits.Value += creditsForResting;
                    AdvanceTurn(); break;
            }
        }

        // ═════════════════════════════════════════════════════════════
        // ACTION PHASE
        // ═════════════════════════════════════════════════════════════

        [ServerRpc(RequireOwnership = false)]
        public void ConfirmShipMovementServerRpc(int destId, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            var player = GetActivePlayer();
            if (ShipMovement.Instance != null)
                ShipMovement.Instance.TryMovePlayer(player, destId);
        }

        [ServerRpc(RequireOwnership = false)]
        public void BuyCardServerRpc(MarketDeckType deckType, int rowIndex, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            DeckManager.Instance?.TryPurchaseCard(GetActivePlayer(), deckType, rowIndex);
        }

        [ServerRpc(RequireOwnership = false)]
        public void CycleCardServerRpc(MarketDeckType deckType, int rowIndex, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            DeckManager.Instance?.TryCycleCard(GetActivePlayer(), deckType, rowIndex);
        }

        [ServerRpc(RequireOwnership = false)]
        public void DeliverCargoServerRpc(int cardId, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            var p = GetActivePlayer();
            p.AddCredits(2000); p.AddFame(1);
            Debug.Log($"[GameManager] Player {p.OwnerClientId} delivered cargo");
        }

        [ServerRpc(RequireOwnership = false)]
        public void CompleteBountyServerRpc(int cardId, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            var p = GetActivePlayer();
            p.AddCredits(5000); p.AddFame(2);
            Debug.Log($"[GameManager] Player {p.OwnerClientId} completed bounty");
        }

        /// <summary>Outer Rim Trade action: transfer credits between players in same space.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void TradeCreditsServerRpc(ulong targetClientId, int amount, ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!ValidateActivePlayer(sender)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            if (amount <= 0) return;

            var from = GetPlayerByClient(sender);
            var to = GetPlayerByClient(targetClientId);
            if (from == null || to == null || from == to) return;

            // Must be in same space (Outer Rim rule)
            if (from.CurrentNodeId.Value != to.CurrentNodeId.Value) return;

            if (!from.SpendCredits(amount)) return;
            to.AddCredits(amount);
            Debug.Log($"[GameManager] Trade: Player {sender} → {targetClientId}: {amount} credits");
        }

        [ServerRpc(RequireOwnership = false)]
        public void EndActionPhaseServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            TransitionToPhase(GamePhase.EncounterPhase);
        }

        // ═════════════════════════════════════════════════════════════
        // ENCOUNTER PHASE
        // ═════════════════════════════════════════════════════════════

        public void NotifyEncounterComplete()
        {
            if (!IsServer) return;
            if (currentPhase.Value != GamePhase.EncounterPhase) return;
            TransitionToPhase(GamePhase.CheckingWinCondition);
        }

        // ═════════════════════════════════════════════════════════════
        // TURN MANAGEMENT
        // ═════════════════════════════════════════════════════════════

        public void AdvanceTurn()
        {
            if (!IsServer) return;
            int next = currentPlayerIndex.Value + 1;
            if (next >= turnOrder.Count) { next = 0; turnNumber.Value++; }
            currentPlayerIndex.Value = next;
            TransitionToPhase(GamePhase.PlanningPhase);
        }

        public PlayerState GetActivePlayer()
        {
            if (turnOrder.Count == 0) return null;
            int i = currentPlayerIndex.Value;
            return i >= 0 && i < turnOrder.Count ? turnOrder[i] : null;
        }

        public PlayerState GetPlayerByClient(ulong clientId)
        {
            foreach (var p in turnOrder)
                if (p.OwnerClientId == clientId) return p;
            return null;
        }

        // ═════════════════════════════════════════════════════════════
        // WIN CONDITION
        // ═════════════════════════════════════════════════════════════

        private void CheckWinCondition()
        {
            if (!IsServer) return;
            foreach (var p in turnOrder)
            {
                if (p.Fame.Value >= fameToWin)
                {
                    currentPhase.Value = GamePhase.GameOver;
                    AnnounceGameOverClientRpc(p.OwnerClientId);
                    OnGameOver?.Invoke(p.OwnerClientId);
                    return;
                }
            }
            AdvanceTurn();
        }

        [ClientRpc] private void AnnounceGameOverClientRpc(ulong id) { OnPhaseChanged?.Invoke(GamePhase.GameOver); OnGameOver?.Invoke(id); }

        private bool ValidateActivePlayer(ulong clientId)
        {
            if (!IsServer) return false;
            var a = GetActivePlayer();
            return a != null && a.OwnerClientId == clientId;
        }

        private void HandlePhaseChanged(GamePhase prev, GamePhase curr) => OnPhaseChanged?.Invoke(curr);
        private void HandleActivePlayerChanged(int prev, int curr)
        {
            if (prev >= 0 && prev < turnOrder.Count) turnOrder[prev].SetTurnActive(false);
            if (curr >= 0 && curr < turnOrder.Count) turnOrder[curr].SetTurnActive(true);
        }

        private void Shuffle<T>(List<T> list)
        { int n = list.Count; while (n > 1) { n--; int k = rng.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); } }
    }
}
