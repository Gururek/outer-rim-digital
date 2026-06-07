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

        // ─── Networked State ─────────────────────────────────────────────────
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

        // ─── Server-Only State ───────────────────────────────────────────────
        private List<PlayerState> turnOrder = new List<PlayerState>();
        private bool planningChoiceMade = false;
        private System.Random rng;

        // ─── Public Accessors ────────────────────────────────────────────────
        public GamePhase CurrentPhase => currentPhase.Value;
        public int CurrentTurnNumber => turnNumber.Value;
        public List<PlayerState> TurnOrder => turnOrder;

        // ─── Events ──────────────────────────────────────────────────────────
        public System.Action<GamePhase> OnPhaseChanged;
        public System.Action<PlayerState> OnTurnStarted;
        public System.Action<ulong> OnGameOver;

        // ═════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═════════════════════════════════════════════════════════════════════

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

        // ═════════════════════════════════════════════════════════════════════
        // GAME START
        // ═════════════════════════════════════════════════════════════════════

        private IEnumerator WaitForPlayersCoroutine()
        {
            yield return new WaitUntil(
                () => NetworkManager.Singleton.ConnectedClients.Count >= minPlayersToStart);

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
                    turnOrder.Add(ps);
            }
            Shuffle(turnOrder);
            currentPlayerIndex.Value = 0;
        }

        // ═════════════════════════════════════════════════════════════════════
        // PHASE STATE MACHINE
        // ═════════════════════════════════════════════════════════════════════

        private void TransitionToPhase(GamePhase newPhase)
        {
            if (!IsServer) return;

            planningChoiceMade = false;
            currentPhase.Value = newPhase;

            var activePlayer = GetActivePlayer();

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

        // ═════════════════════════════════════════════════════════════════════
        // PLANNING PHASE
        // ═════════════════════════════════════════════════════════════════════

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

        // ═════════════════════════════════════════════════════════════════════
        // ACTION PHASE
        // ═════════════════════════════════════════════════════════════════════

        [ServerRpc(RequireOwnership = false)]
        public void EndActionPhaseServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!ValidateActivePlayer(rpcParams.Receive.SenderClientId)) return;
            if (currentPhase.Value != GamePhase.ActionPhase) return;

            TransitionToPhase(GamePhase.EncounterPhase);
        }

        // ═════════════════════════════════════════════════════════════════════
        // TURN MANAGEMENT
        // ═════════════════════════════════════════════════════════════════════

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

        // ═════════════════════════════════════════════════════════════════════
        // WIN CONDITION
        // ═════════════════════════════════════════════════════════════════════

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

            // No winner yet — advance to next player
            AdvanceTurn();
        }

        [ClientRpc]
        private void AnnounceGameOverClientRpc(ulong winnerClientId)
        {
            OnPhaseChanged?.Invoke(GamePhase.GameOver);
            OnGameOver?.Invoke(winnerClientId);
        }

        // ═════════════════════════════════════════════════════════════════════
        // VALIDATION
        // ═════════════════════════════════════════════════════════════════════

        private bool ValidateActivePlayer(ulong senderClientId)
        {
            if (!IsServer) return false;
            var active = GetActivePlayer();
            if (active == null) return false;
            return active.OwnerClientId == senderClientId;
        }

        // ═════════════════════════════════════════════════════════════════════
        // NETWORK VARIABLE HANDLERS
        // ═════════════════════════════════════════════════════════════════════

        private void HandlePhaseChanged(GamePhase previous, GamePhase current)
        {
            OnPhaseChanged?.Invoke(current);
        }

        private void HandleActivePlayerChanged(int previous, int current)
        {
            // Mark previous player inactive, current active
            if (previous >= 0 && previous < turnOrder.Count)
                turnOrder[previous].SetTurnActive(false);
            if (current >= 0 && current < turnOrder.Count)
                turnOrder[current].SetTurnActive(true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════════

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
