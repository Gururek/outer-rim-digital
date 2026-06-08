// GameManager.cs — Central game state machine authority (server-only)
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

        private NetworkVariable<GamePhase> currentPhase = new NetworkVariable<GamePhase>(
            GamePhase.WaitingForPlayers,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private NetworkVariable<int> currentPlayerIndex = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private NetworkVariable<int> turnNumber = new NetworkVariable<int>(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private List<PlayerState> turnOrder = new List<PlayerState>();
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
            Debug.Log($"[GameManager] WaitForPlayers: waiting for {minPlayersToStart} players. Connected: {NetworkManager.Singleton.ConnectedClients.Count}");
            yield return new WaitUntil(
                () => NetworkManager.Singleton.ConnectedClients.Count >= minPlayersToStart);

            yield return new WaitForSeconds(0.5f);

            CollectAndOrderPlayers();
            Debug.Log($"[GameManager] Players collected: {turnOrder.Count}. Transitioning to PlanningPhase.");
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
            Debug.Log($"[GameManager] TransitionToPhase: {newPhase}");

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

        [ClientRpc]
        private void BeginPlanningPhaseClientRpc(ulong activeClientId)
        {
            bool isMyTurn = activeClientId == NetworkManager.Singleton.LocalClientId;
            OnPhaseChanged?.Invoke(GamePhase.PlanningPhase);
        }

        [ClientRpc]
        private void BeginActionPhaseClientRpc(ulong activeClientId)
        {
            OnPhaseChanged?.Invoke(GamePhase.ActionPhase);
        }

        [ClientRpc]
        private void BeginEncounterPhaseClientRpc(ulong activeClientId)
        {
            OnPhaseChanged?.Invoke(GamePhase.EncounterPhase);
        }

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

        [ServerRpc(RequireOwnership = false)]
        public void EndActionPhaseServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;

            TransitionToPhase(GamePhase.EncounterPhase);
        }

        public void NotifyEncounterComplete()
        {
            if (!IsServer) return;
            if (currentPhase.Value != GamePhase.EncounterPhase) return;
            TransitionToPhase(GamePhase.CheckingWinCondition);
        }

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

        private void HandlePhaseChanged(GamePhase previous, GamePhase current)
        {
            OnPhaseChanged?.Invoke(current);
        }

        private void HandleActivePlayerChanged(int previous, int current)
        {
            if (previous >= 0 && previous < turnOrder.Count)
                turnOrder[previous].SetTurnActive(false);
            if (current >= 0 && current < turnOrder.Count)
                turnOrder[current].SetTurnActive(true);
        }

        private void Shuffle<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }
    }
}
