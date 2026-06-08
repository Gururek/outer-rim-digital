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
            Debug.Log($"[GameManager] Awake. IsServer={IsServer}, IsClient={IsClient}");
        }

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[GameManager] OnNetworkSpawn. IsServer={IsServer}");
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
            Debug.Log($"[GameManager] Waiting for {minPlayersToStart} players. Connected: {NetworkManager.Singleton.ConnectedClients.Count}");
            yield return new WaitUntil(() => NetworkManager.Singleton.ConnectedClients.Count >= minPlayersToStart);
            yield return new WaitForSeconds(0.5f);
            CollectAndOrderPlayers();
            Debug.Log($"[GameManager] Players: {turnOrder.Count}. Starting PlanningPhase.");
            TransitionToPhase(GamePhase.PlanningPhase);
        }

        private void CollectAndOrderPlayers()
        {
            turnOrder.Clear();
            foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
            {
                var playerObj = kvp.Value.PlayerObject;
                if (playerObj != null && playerObj.TryGetComponent(out PlayerState ps))
                {
                    ps.CurrentNodeId.Value = 0;
                    turnOrder.Add(ps);
                }
                else
                {
                    var go = new GameObject($"Player_{kvp.Key}");
                    var netObj = go.AddComponent<NetworkObject>();
                    var newPs = go.AddComponent<PlayerState>();
                    netObj.SpawnWithOwnership(kvp.Key);
                    newPs.CurrentNodeId.Value = 0;
                    turnOrder.Add(newPs);
                    Debug.Log($"[GameManager] Auto-spawned PlayerState for client {kvp.Key}");
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
            Debug.Log($"[GameManager] Phase -> {newPhase}");

            // Outer Rim rule: defeated players must choose Heal during Planning Step
            if (newPhase == GamePhase.PlanningPhase)
            {
                var ap = GetActivePlayer();
                if (ap != null && ap.IsDefeated.Value)
                {
                    Debug.Log($"[GameManager] Player {ap.OwnerClientId} is defeated — auto-healing.");
                    ap.Health.Value = ap.MaxHealth.Value;
                    ap.ShipHealth.Value = ap.MaxShipHealth.Value;
                    ap.RecoverFromDefeat();
                    AdvanceTurn();
                    return;
                }
            }

            var activePlayer = GetActivePlayer();
            if (activePlayer == null && newPhase != GamePhase.CheckingWinCondition)
            {
                Debug.LogWarning($"[GameManager] No active player for phase {newPhase} — skipping.");
                return;
            }

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
        // PLANNING PHASE (Outer Rim: choose 1 — Move, Heal, Credits)
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
                case PlanningChoice.MoveShip:
                    TransitionToPhase(GamePhase.ActionPhase);
                    break;
                case PlanningChoice.HealDamage:
                    player.Health.Value = player.MaxHealth.Value;
                    player.ShipHealth.Value = player.MaxShipHealth.Value;
                    AdvanceTurn();
                    break;
                case PlanningChoice.CollectCredits:
                    player.Credits.Value += creditsForResting;
                    AdvanceTurn();
                    break;
            }
        }

        // ═════════════════════════════════════════════════════════════
        // ACTION PHASE (Outer Rim: any/all — Move, Market, Trade, Deliver, Job)
        // ═════════════════════════════════════════════════════════════

        [ServerRpc(RequireOwnership = false)]
        public void ConfirmShipMovementServerRpc(int destinationNodeId, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            var player = GetActivePlayer();
            if (ShipMovement.Instance != null)
                ShipMovement.Instance.TryMovePlayer(player, destinationNodeId);
        }

        [ServerRpc(RequireOwnership = false)]
        public void BuyCardServerRpc(MarketDeckType deckType, int rowIndex, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            var player = GetActivePlayer();
            DeckManager.Instance?.TryPurchaseCard(player, deckType, rowIndex);
        }

        [ServerRpc(RequireOwnership = false)]
        public void CycleCardServerRpc(MarketDeckType deckType, int rowIndex, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            var player = GetActivePlayer();
            DeckManager.Instance?.TryCycleCard(player, deckType, rowIndex);
        }

        /// <summary>Deliver cargo to current planet for reward (Outer Rim Deliver action).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void DeliverCargoServerRpc(int cargoCardId, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            var player = GetActivePlayer();
            // Deliver action: remove cargo from inventory, gain credits + fame based on card
            // Simplified: gain 2000 credits + 1 fame per delivery
            player.AddCredits(2000);
            player.AddFame(1);
            Debug.Log($"[GameManager] Player {player.OwnerClientId} delivered cargo for +2000cr +1 fame");
        }

        /// <summary>Complete a bounty at current planet (Outer Rim: find contact, fight, capture).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CompleteBountyServerRpc(int bountyCardId, ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;
            var player = GetActivePlayer();
            // Simplified: gain bounty reward
            player.AddCredits(5000);
            player.AddFame(2);
            Debug.Log($"[GameManager] Player {player.OwnerClientId} completed bounty for +5000cr +2 fame");
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
            int nextIndex = currentPlayerIndex.Value + 1;
            if (nextIndex >= turnOrder.Count)
            {
                nextIndex = 0;
                turnNumber.Value++;
            }
            currentPlayerIndex.Value = nextIndex;
            TransitionToPhase(GamePhase.PlanningPhase);
        }

        public PlayerState GetActivePlayer()
        {
            if (turnOrder.Count == 0) return null;
            int index = currentPlayerIndex.Value;
            return index >= 0 && index < turnOrder.Count ? turnOrder[index] : null;
        }

        // ═════════════════════════════════════════════════════════════
        // WIN CONDITION (Outer Rim: fame >= threshold)
        // ═════════════════════════════════════════════════════════════

        private void CheckWinCondition()
        {
            if (!IsServer) return;
            foreach (var player in turnOrder)
            {
                if (player.Fame.Value >= fameToWin)
                {
                    currentPhase.Value = GamePhase.GameOver;
                    AnnounceGameOverClientRpc(player.OwnerClientId);
                    OnGameOver?.Invoke(player.OwnerClientId);
                    return;
                }
            }
            AdvanceTurn();
        }

        [ClientRpc]
        private void AnnounceGameOverClientRpc(ulong winnerClientId)
        {
            OnPhaseChanged?.Invoke(GamePhase.GameOver);
            OnGameOver?.Invoke(winnerClientId);
        }

        private bool ValidateActivePlayer(ulong senderClientId)
        {
            if (!IsServer) return false;
            var active = GetActivePlayer();
            if (active == null) return false;
            return active.OwnerClientId == senderClientId;
        }

        private void HandlePhaseChanged(GamePhase prev, GamePhase curr) => OnPhaseChanged?.Invoke(curr);

        private void HandleActivePlayerChanged(int prev, int curr)
        {
            if (prev >= 0 && prev < turnOrder.Count) turnOrder[prev].SetTurnActive(false);
            if (curr >= 0 && curr < turnOrder.Count) turnOrder[curr].SetTurnActive(true);
        }

        private void Shuffle<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1) { n--; int k = rng.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); }
        }
    }
}
