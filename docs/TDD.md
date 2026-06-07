# Technical Design Document
## Star Wars: Outer Rim — Digital Edition (Working Title)
### Unity 3D | Turn-Based Multiplayer Board Game

**Version:** 1.0  
**Engine:** Unity 2022.3 LTS (or later)  
**Networking Stack:** Unity Netcode for GameObjects (NGO) + Unity Relay + Unity Lobby  
**Language:** C# (.NET Standard 2.1)

---

## Table of Contents
1. [Multiplayer Architecture](#1-multiplayer-architecture)
2. [Project File Structure](#2-project-file-structure)
3. [Core Class Structure — Game State Machine](#3-core-class-structure)
4. [Node Movement & Pathfinding](#4-node-movement--pathfinding)
5. [Market Deck System](#5-market-deck-system)
6. [Supporting Systems — Dice & Combat](#6-supporting-systems)
7. [ScriptableObject Definitions](#7-scriptableobject-definitions)
8. [Implementation Roadmap](#8-implementation-roadmap)

---

## 1. Multiplayer Architecture

### 1.1 Recommended Stack: NGO + Unity Relay (Host-Server Model)

For a **turn-based board game** with 2–4 players, a **dedicated, always-on server is overkill**. 
The recommended approach is a **Host-Server model using Unity Relay**, where one player's 
machine acts as the authoritative server while Unity Relay brokers the connection to bypass 
NAT traversal — so no port forwarding or public IP is required.

```
┌─────────────────────────────────────────────────────────────┐
│                     UNITY SERVICES                          │
│  ┌─────────────────┐        ┌───────────────────────────┐  │
│  │  Unity Lobby    │        │     Unity Relay           │  │
│  │  (session mgmt) │        │  (NAT-bypassing broker)   │  │
│  └────────┬────────┘        └──────────┬────────────────┘  │
└───────────┼──────────────────────────-─┼────────────────────┘
            │                            │
    ┌───────▼────────────────────────────▼───────┐
    │         HOST PLAYER MACHINE                │
    │  ┌────────────────┐  ┌──────────────────┐  │
    │  │  NGO Server    │  │  NGO Client      │  │
    │  │  (Authority)   │  │  (Local Player)  │  │
    │  └───────┬────────┘  └──────────────────┘  │
    └──────────┼─────────────────────────────────┘
               │ (via Relay)
    ┌──────────┼─────────────────────────────────┐
    │ REMOTE   │  CLIENT 2       CLIENT 3         │
    │  ┌───────▼──────┐    ┌────────────────┐    │
    │  │  NGO Client  │    │   NGO Client   │    │
    │  └──────────────┘    └────────────────┘    │
    └─────────────────────────────────────────────┘
```

**Why not a dedicated server?**  
Turn-based games have very low bandwidth and latency requirements. State changes only occur 
once per player per turn. A Relay-hosted model costs nothing at small scale. Dedicated servers 
(via Unity Game Server Hosting) can be adopted later if anti-cheat or host-quitting becomes 
a significant issue.

**Why not Photon PUN2?**  
Unity NGO + UGS is the first-party stack, fully maintained by Unity, with better pricing 
transparency. It integrates cleanly with the Lobby and Economy services you may need later.

### 1.2 Authority Model

```
CLIENT ACTION                    SERVER (HOST)
─────────────────────────────────────────────────────
Player clicks "Heal"      →   SubmitPlanningChoiceServerRpc()
                              └─ Validates: correct player, correct phase
                              └─ Mutates NetworkVariables
                              └─ Calls NotifyPhaseStartClientRpc()
                                         ↓ (broadcasts to all)
All clients receive       ←   NetworkVariable change events
updated UI                    ClientRpc animation triggers
```

**Key principle:** Clients *request* state changes via `[ServerRpc]`. Only the server mutates 
`NetworkVariable` values. Clients react to value changes via event subscriptions.

### 1.3 State Synchronization Strategy

| Data | Sync Method | Reason |
|---|---|---|
| Game phase, turn index | `NetworkVariable<T>` | All clients must always know this |
| Player Fame, Credits, Health | `NetworkVariable<int>` | Frequently read by all for UI |
| Player reputation (×4 factions) | `NetworkVariable<int>` ×4 | Infrequently changed, always visible |
| Player node location | `NetworkVariable<int>` | Needed for movement rendering |
| Player inventory (cargo, crew, etc.) | `NetworkList<CardInstanceData>` | Dynamic collection, synced |
| Deck contents | Server-only `List<T>` | Hidden from clients (anti-cheat) |
| Market row cards | Server-→-Client via ClientRpc | Revealed only when appropriate |
| Dice roll results | `ClientRpc` payload | Determined server-side, broadcast |
| Ship animations, VFX | `ClientRpc` | Pure presentation, no gameplay state |

### 1.4 Session Management Flow

```
1. HOST: Creates Lobby (Unity Lobby API)
         → Allocates Relay server (Unity Relay API)
         → Embeds JoinCode in Lobby data
         → Starts NGO Host

2. CLIENT: Queries Lobby (by code or list)
           → Retrieves JoinCode from Lobby data  
           → Connects to Relay allocation
           → Starts NGO Client

3. NGO: OnClientConnectedCallback fires on host
        → Once all players present: NetworkManager spawns GameManager
        → GameManager.StartGame() initializes all server-side state

4. MID-GAME: If host disconnects → Show "Host Left" UI
             → Optionally implement host migration (advanced)
```

---

## 2. Project File Structure

```
Assets/
├── _Game/
│   ├── Scripts/
│   │   ├── Core/
│   │   │   ├── GameManager.cs          ← State machine authority
│   │   │   ├── PlayerState.cs          ← Per-player networked state
│   │   │   ├── TurnManager.cs          ← Turn ordering helper
│   │   │   └── GameEvents.cs           ← Typed event bus (ScriptableObject events)
│   │   ├── Map/
│   │   │   ├── MapNode.cs              ← Node data + occupancy
│   │   │   ├── MapManager.cs           ← Graph registry + BFS pathfinding
│   │   │   └── MapNodeVisuals.cs       ← Highlight, click detection
│   │   ├── Cards/
│   │   │   ├── CardData.cs             ← SO base class
│   │   │   ├── BountyCardData.cs       ← SO: Bounty specifics
│   │   │   ├── CargoCardData.cs        ← SO: Cargo specifics
│   │   │   ├── GearCardData.cs         ← SO: Gear specifics
│   │   │   ├── ModCardData.cs          ← SO: Ship mod specifics
│   │   │   ├── JobCardData.cs          ← SO: Job specifics
│   │   │   ├── LuxuryCardData.cs       ← SO: Luxury specifics
│   │   │   ├── EncounterCardData.cs    ← SO: Planet encounter cards
│   │   │   ├── MarketDeck.cs           ← Runtime deck class (not SO)
│   │   │   └── DeckManager.cs          ← All 6 decks + encounter decks
│   │   ├── Characters/
│   │   │   ├── ScoundrelData.cs        ← SO: Character definition
│   │   │   └── ShipData.cs             ← SO: Ship definition
│   │   ├── Combat/
│   │   │   ├── DiceFaceDistribution.cs ← SO: Die face weightings
│   │   │   ├── DiceRoller.cs           ← Static roll logic
│   │   │   ├── CombatResolver.cs       ← Compares rolls, applies damage
│   │   │   └── PatrolEnemy.cs          ← AI patrol data class
│   │   ├── Encounter/
│   │   │   └── EncounterResolver.cs    ← Handles planet/contact/patrol events
│   │   ├── UI/
│   │   │   └── UIManager.cs            ← Phase display, notifications
│   │   └── Networking/
│   │       └── NetworkBootstrapper.cs  ← Relay + Lobby connection logic
│   ├── ScriptableObjects/
│   │   ├── Characters/                 ← All ScoundrelData assets
│   │   ├── Ships/                      ← All ShipData assets
│   │   ├── Cards/
│   │   │   ├── Bounties/
│   │   │   ├── Cargo/
│   │   │   ├── Gear/
│   │   │   ├── Mods/
│   │   │   ├── Jobs/
│   │   │   ├── Luxury/
│   │   │   └── Encounters/
│   │   └── Dice/
│   ├── Prefabs/
│   │   ├── Networking/
│   │   │   └── NetworkGameManager.prefab
│   │   ├── Map/
│   │   │   ├── PlanetNode.prefab
│   │   │   └── NavpointNode.prefab
│   │   └── Ships/                      ← 3D ship prefabs per character
│   └── Resources/
│       └── CardData/                   ← JSON files (if using JSON pipeline)
```

---

## 3. Core Class Structure

### 3.1 Enumerations & Shared Types

```csharp
// Enums.cs
// Shared enumerations used across the project.

public enum GamePhase
{
    WaitingForPlayers,
    PlanningPhase,
    ActionPhase,
    EncounterPhase,
    ResolvingCombat,
    CheckingWinCondition,
    GameOver
}

public enum PlanningChoice
{
    None,
    MoveShip,       // Move ship up to Speed stat
    HealDamage,     // Restore all Scoundrel + Hull health
    CollectCredits  // Gain 2,000 credits
}

public enum MarketDeckType
{
    Bounties,
    Cargo,
    Gear,
    Mods,
    Jobs,
    Luxury
}

public enum FactionType
{
    Syndicate,      // e.g. criminal underworld
    Authority,      // e.g. imperial/government forces
    Rebels,         // e.g. resistance faction
    Hutts,          // e.g. crime lords
    None
}

public enum ReputationStatus
{
    Negative = -1,
    Neutral  =  0,
    Positive =  1
}

public enum MapNodeType
{
    Planet,
    NavPoint
}

public enum SkillType
{
    Combat,
    Piloting,
    Cunning,
    Tech
}

public enum DieFace
{
    Blank,
    Focus,
    Hit,
    Crit
}

// ─────────────────────────────────────────────────────────────────────────────
// Serializable struct for card instances stored in NetworkLists.
// Must implement INetworkSerializable and IEquatable for NGO compatibility.
// ─────────────────────────────────────────────────────────────────────────────
[System.Serializable]
public struct CardInstanceData : INetworkSerializable, System.IEquatable<CardInstanceData>
{
    public int CardDefinitionId;   // Foreign key → CardData SO or JSON record
    public MarketDeckType DeckType;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref CardDefinitionId);
        int deckTypeInt = (int)DeckType;
        serializer.SerializeValue(ref deckTypeInt);
        DeckType = (MarketDeckType)deckTypeInt;
    }

    public bool Equals(CardInstanceData other) =>
        CardDefinitionId == other.CardDefinitionId && DeckType == other.DeckType;

    public override int GetHashCode() =>
        System.HashCode.Combine(CardDefinitionId, (int)DeckType);
}
```

---

### 3.2 `GameManager.cs`

```csharp
// GameManager.cs
// Authority: Server (Host)
// Responsibilities:
//   - Owns the game state machine (phase transitions)
//   - Validates all player turn actions
//   - Broadcasts phase changes and events to clients
//   - Checks win conditions after each turn

using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    public static GameManager Instance { get; private set; }

    // ─── Inspector Config ────────────────────────────────────────────────────
    [Header("Win Condition")]
    [SerializeField] private int fameToWin = 10;

    [Header("Planning Phase Values")]
    [SerializeField] private int creditsForResting = 2000;

    [Header("Minimum Players")]
    [SerializeField] private int minPlayersToStart = 2;

    // ─── Networked State (Server writes, all clients read) ───────────────────
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

    // ─── Server-Only State (not synced; clients get updates via ClientRpc) ───
    private List<PlayerState> turnOrder = new List<PlayerState>();
    private bool planningChoiceMade = false;

    // ─── Public Events (subscribed to by UI, audio, etc.) ───────────────────
    public event System.Action<GamePhase> OnPhaseChanged;
    public event System.Action<PlayerState> OnTurnStarted;
    public event System.Action<ulong>      OnGameOver;    // winnerClientId

    // ─── Public Accessors ────────────────────────────────────────────────────
    public GamePhase CurrentPhase      => currentPhase.Value;
    public int       CurrentTurnNumber => turnNumber.Value;

    // ═════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    public override void OnNetworkSpawn()
    {
        // All clients: subscribe to network variable changes for UI updates.
        currentPhase.OnValueChanged       += HandlePhaseChanged;
        currentPlayerIndex.OnValueChanged += HandleActivePlayerChanged;

        if (!IsServer) return;
        // Server: wait for all players to connect, then begin.
        StartCoroutine(WaitForPlayersCoroutine());
    }

    public override void OnNetworkDespawn()
    {
        currentPhase.OnValueChanged       -= HandlePhaseChanged;
        currentPlayerIndex.OnValueChanged -= HandleActivePlayerChanged;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GAME START (Server)
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator WaitForPlayersCoroutine()
    {
        yield return new WaitUntil(
            () => NetworkManager.Singleton.ConnectedClients.Count >= minPlayersToStart);

        yield return new WaitForSeconds(0.5f); // Brief delay for all clients to finish spawning

        CollectAndOrderPlayers();
        TransitionToPhase(GamePhase.PlanningPhase);
    }

    /// <summary>
    /// Server collects all PlayerState components from spawned player objects
    /// and randomizes turn order.
    /// </summary>
    private void CollectAndOrderPlayers()
    {
        turnOrder.Clear();

        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            var playerObj = kvp.Value.PlayerObject;
            if (playerObj != null && playerObj.TryGetComponent(out PlayerState ps))
                turnOrder.Add(ps);
        }

        turnOrder.Shuffle(); // Extension method at bottom of file
        currentPlayerIndex.Value = 0;

        Debug.Log($"[GameManager] Turn order set: {string.Join(", ",
            turnOrder.ConvertAll(p => p.OwnerClientId))}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PHASE STATE MACHINE (Server)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Central state machine transition. Only callable on the server.
    /// Resets per-phase flags and notifies all clients.
    /// </summary>
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
                EncounterResolver.Instance?.ResolveEncounter(activePlayer);
                break;

            case GamePhase.CheckingWinCondition:
                CheckWinCondition();
                break;

            case GamePhase.GameOver:
                // Handled within CheckWinCondition
                break;
        }
    }

    // ── Client Notification RPCs ─────────────────────────────────────────────

    [ClientRpc]
    private void BeginPlanningPhaseClientRpc(ulong activeClientId)
    {
        bool isMyTurn = activeClientId == NetworkManager.Singleton.LocalClientId;
        UIManager.Instance?.ShowPlanningPhase(isMyTurn);
        OnPhaseChanged?.Invoke(GamePhase.PlanningPhase);
    }

    [ClientRpc]
    private void BeginActionPhaseClientRpc(ulong activeClientId)
    {
        bool isMyTurn = activeClientId == NetworkManager.Singleton.LocalClientId;
        UIManager.Instance?.ShowActionPhase(isMyTurn);
        OnPhaseChanged?.Invoke(GamePhase.ActionPhase);
    }

    [ClientRpc]
    private void BeginEncounterPhaseClientRpc(ulong activeClientId)
    {
        bool isMyTurn = activeClientId == NetworkManager.Singleton.LocalClientId;
        UIManager.Instance?.ShowEncounterPhase(isMyTurn);
        OnPhaseChanged?.Invoke(GamePhase.EncounterPhase);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PLANNING PHASE — Player Submits Their Choice
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by the active client to declare their Planning Phase choice.
    /// Server validates, then executes the effect or enables movement mode.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SubmitPlanningChoiceServerRpc(PlanningChoice choice, ServerRpcParams rpcParams = default)
    {
        // Guard: only accept from the correct player, in the correct phase.
        if (!ValidateActivePlayerRpc(rpcParams.Receive.SenderClientId)) return;
        if (currentPhase.Value != GamePhase.PlanningPhase)             return;
        if (planningChoiceMade)                                         return;

        planningChoiceMade = true;
        var activePlayer = GetActivePlayer();

        switch (choice)
        {
            case PlanningChoice.MoveShip:
                // Tell the active client to highlight valid nodes and await a move selection.
                // Actual movement is confirmed via ConfirmShipMovementServerRpc.
                ActivateMovementSelectionClientRpc(
                    activePlayer.OwnerClientId,
                    activePlayer.CurrentNodeId,
                    activePlayer.GetEffectiveSpeed());
                break;

            case PlanningChoice.HealDamage:
                activePlayer.HealAll();
                HealAnimationClientRpc(activePlayer.OwnerClientId);
                TransitionToPhase(GamePhase.ActionPhase);
                break;

            case PlanningChoice.CollectCredits:
                activePlayer.AddCredits(creditsForResting);
                TransitionToPhase(GamePhase.ActionPhase);
                break;
        }
    }

    [ClientRpc]
    private void ActivateMovementSelectionClientRpc(ulong activeClientId, int startNodeId, int speed)
    {
        if (NetworkManager.Singleton.LocalClientId != activeClientId) return;
        MapManager.Instance?.ShowReachableNodes(startNodeId, speed);
    }

    [ClientRpc]
    private void HealAnimationClientRpc(ulong ownerClientId)
    {
        // Trigger heal VFX on the relevant ship/character model.
        // UIManager.Instance?.ShowHealEffect(ownerClientId);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MOVEMENT CONFIRMATION (Planning Phase, MoveShip path)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called after the active client selects a destination node.
    /// Server validates the move is legal, then executes it.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ConfirmShipMovementServerRpc(int destinationNodeId, ServerRpcParams rpcParams = default)
    {
        if (!ValidateActivePlayerRpc(rpcParams.Receive.SenderClientId)) return;
        if (currentPhase.Value != GamePhase.PlanningPhase)             return;

        var activePlayer = GetActivePlayer();

        // Validate: can we reach the destination within speed?
        var path = MapManager.Instance?.FindPath(
            activePlayer.CurrentNodeId, destinationNodeId, activePlayer.GetEffectiveSpeed());

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"[GameManager] Invalid move attempt to node {destinationNodeId}");
            return;
        }

        // Execute move on server state.
        activePlayer.MoveToNode(destinationNodeId);

        // Trigger animation on all clients, then advance phase.
        AnimateShipMovementClientRpc(activePlayer.OwnerClientId, path.ToArray());

        // Delay the phase transition slightly to let animation play.
        StartCoroutine(DelayedPhaseTransition(GamePhase.ActionPhase, 1.5f));
    }

    [ClientRpc]
    private void AnimateShipMovementClientRpc(ulong playerClientId, int[] pathNodeIds)
    {
        MapManager.Instance?.AnimateShipAlongPath(playerClientId, pathNodeIds);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ACTION PHASE — Player Signals Done
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by the active client when they are done with all their Action 
    /// Phase actions (buying cards, delivering cargo, trading, etc.).
    /// Individual actions (buy, deliver, trade) are separate ServerRpcs on 
    /// their respective managers, not here.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void EndActionPhaseServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!ValidateActivePlayerRpc(rpcParams.Receive.SenderClientId)) return;
        if (currentPhase.Value != GamePhase.ActionPhase)                return;

        TransitionToPhase(GamePhase.EncounterPhase);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ENCOUNTER PHASE — Called by EncounterResolver when done
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by EncounterResolver when all encounter effects have been applied.
    /// </summary>
    public void NotifyEncounterComplete()
    {
        if (!IsServer) return;
        TransitionToPhase(GamePhase.CheckingWinCondition);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // WIN CONDITION CHECK (Server)
    // ═════════════════════════════════════════════════════════════════════════

    private void CheckWinCondition()
    {
        if (!IsServer) return;

        foreach (var player in turnOrder)
        {
            if (player.Fame >= fameToWin)
            {
                currentPhase.Value = GamePhase.GameOver;
                DeclareWinnerClientRpc(player.OwnerClientId);
                OnGameOver?.Invoke(player.OwnerClientId);
                return;
            }
        }

        // No winner yet — advance to next player's turn.
        AdvanceTurn();
    }

    private void AdvanceTurn()
    {
        if (!IsServer) return;

        int nextIndex = (currentPlayerIndex.Value + 1) % turnOrder.Count;
        currentPlayerIndex.Value = nextIndex;

        // Increment turn counter when we cycle back to first player.
        if (nextIndex == 0)
            turnNumber.Value++;

        TransitionToPhase(GamePhase.PlanningPhase);
    }

    [ClientRpc]
    private void DeclareWinnerClientRpc(ulong winnerClientId)
    {
        bool localPlayerWon = winnerClientId == NetworkManager.Singleton.LocalClientId;
        UIManager.Instance?.ShowGameOver(localPlayerWon, winnerClientId);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    public PlayerState GetActivePlayer()
    {
        if (turnOrder.Count == 0) return null;
        int idx = Mathf.Clamp(currentPlayerIndex.Value, 0, turnOrder.Count - 1);
        return turnOrder[idx];
    }

    /// <summary>
    /// Returns true if the sending client is the currently active player.
    /// All ServerRpcs that require the active player should call this first.
    /// </summary>
    private bool ValidateActivePlayerRpc(ulong senderClientId)
    {
        var activePlayer = GetActivePlayer();
        if (activePlayer == null) return false;
        if (activePlayer.OwnerClientId != senderClientId)
        {
            Debug.LogWarning($"[GameManager] Out-of-turn action from client {senderClientId}");
            return false;
        }
        return true;
    }

    private IEnumerator DelayedPhaseTransition(GamePhase nextPhase, float delay)
    {
        yield return new WaitForSeconds(delay);
        TransitionToPhase(nextPhase);
    }

    private void HandlePhaseChanged(GamePhase _, GamePhase newPhase)
    {
        if (!IsServer) OnPhaseChanged?.Invoke(newPhase); // Server already fired its local event
    }

    private void HandleActivePlayerChanged(int _, int newIndex) { /* UI hook */ }
}

// ─── List Extension ──────────────────────────────────────────────────────────
public static class ListExtensions
{
    public static void Shuffle<T>(this List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
```

---

### 3.3 `PlayerState.cs`

```csharp
// PlayerState.cs
// Authority: Server
// Responsibilities:
//   - Holds all per-player mutable state (Fame, Credits, Health, Location, Reputation)
//   - Exposes safe mutation methods (server-only guard clauses)
//   - Syncs inventory via NetworkLists

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerState : NetworkBehaviour
{
    // ─── ScriptableObject References (assigned in Prefab or at runtime) ──────
    // These are the same on all clients (content-addressable by index/name).
    [Header("Character Setup (set before game start)")]
    [SerializeField] private ScoundrelData scoundrelData;
    [SerializeField] private ShipData shipData;

    // ─── Synced via NetworkVariable (everyone can read, server writes) ────────
    private NetworkVariable<int> fame = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> credits = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> scoundrelHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> shipHullHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> currentNodeId = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Reputation: 4 factions. Using named vars for inspector clarity.
    // Range: -3 (hostile) to +3 (allied). 0 = neutral.
    private NetworkVariable<int> repSyndicate  = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> repAuthority  = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> repRebels     = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> repHutts      = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── Inventory (dynamic collections, synced via NetworkList) ─────────────
    // All use CardInstanceData (INetworkSerializable struct).
    private NetworkList<CardInstanceData> cargoHold;
    private NetworkList<CardInstanceData> crewMembers;
    private NetworkList<CardInstanceData> shipMods;
    private NetworkList<CardInstanceData> gearItems;
    private NetworkList<CardInstanceData> activeJobs;
    private NetworkList<CardInstanceData> activeBounties;

    // ─── Locally Cached Stats (same data on all clients from SOs) ────────────
    public ScoundrelStats Stats { get; private set; }
    public ShipStats      Ship  { get; private set; }

    // ─── Public Read-Only Accessors ───────────────────────────────────────────
    public int  Fame           => fame.Value;
    public int  Credits        => credits.Value;
    public int  Health         => scoundrelHealth.Value;
    public int  HullHealth     => shipHullHealth.Value;
    public int  CurrentNodeId  => currentNodeId.Value;

    // ─── Events (local — subscribe for UI updates) ────────────────────────────
    public event System.Action<int>            OnFameChanged;
    public event System.Action<int>            OnCreditsChanged;
    public event System.Action<int>            OnHealthChanged;
    public event System.Action<int>            OnHullChanged;
    public event System.Action<FactionType, int> OnReputationChanged;

    // ═════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // NetworkLists must be instantiated in Awake (before OnNetworkSpawn).
        cargoHold       = new NetworkList<CardInstanceData>();
        crewMembers     = new NetworkList<CardInstanceData>();
        shipMods        = new NetworkList<CardInstanceData>();
        gearItems       = new NetworkList<CardInstanceData>();
        activeJobs      = new NetworkList<CardInstanceData>();
        activeBounties  = new NetworkList<CardInstanceData>();
    }

    public override void OnNetworkSpawn()
    {
        LoadStatsFromScriptableObjects();

        // Subscribe to network variable change events for UI reactivity.
        fame.OnValueChanged          += (_, v) => OnFameChanged?.Invoke(v);
        credits.OnValueChanged       += (_, v) => OnCreditsChanged?.Invoke(v);
        scoundrelHealth.OnValueChanged += (_, v) => OnHealthChanged?.Invoke(v);
        shipHullHealth.OnValueChanged  += (_, v) => OnHullChanged?.Invoke(v);

        repSyndicate.OnValueChanged  += (_, v) => OnReputationChanged?.Invoke(FactionType.Syndicate, v);
        repAuthority.OnValueChanged  += (_, v) => OnReputationChanged?.Invoke(FactionType.Authority, v);
        repRebels.OnValueChanged     += (_, v) => OnReputationChanged?.Invoke(FactionType.Rebels, v);
        repHutts.OnValueChanged      += (_, v) => OnReputationChanged?.Invoke(FactionType.Hutts, v);

        if (!IsServer) return;

        // Initialize mutable state from SO defaults.
        scoundrelHealth.Value = Stats.MaxHealth;
        shipHullHealth.Value  = Ship.MaxHullHealth;
        credits.Value         = scoundrelData != null ? scoundrelData.StartingCredits : 1000;
        fame.Value            = 0;

        // Place ship at the starting node (node 0 or from SO).
        currentNodeId.Value = scoundrelData != null ? scoundrelData.StartingNodeId : 0;
    }

    private void LoadStatsFromScriptableObjects()
    {
        if (scoundrelData != null)
        {
            Stats = new ScoundrelStats(scoundrelData);
        }
        else
        {
            Debug.LogWarning($"[PlayerState] No ScoundrelData assigned for client {OwnerClientId}");
            Stats = ScoundrelStats.Default();
        }

        if (shipData != null)
        {
            Ship = new ShipStats(shipData);
        }
        else
        {
            Debug.LogWarning($"[PlayerState] No ShipData assigned for client {OwnerClientId}");
            Ship = ShipStats.Default();
        }
    }

    public override void OnNetworkDespawn()
    {
        // Dispose NetworkLists to free native memory.
        cargoHold?.Dispose();
        crewMembers?.Dispose();
        shipMods?.Dispose();
        gearItems?.Dispose();
        activeJobs?.Dispose();
        activeBounties?.Dispose();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // FAME
    // ═════════════════════════════════════════════════════════════════════════

    public void AddFame(int amount)
    {
        if (!IsServer) return;
        fame.Value = Mathf.Clamp(fame.Value + amount, 0, 99);
        ShowFameNotificationClientRpc(amount);
    }

    [ClientRpc]
    private void ShowFameNotificationClientRpc(int amount)
    {
        UIManager.Instance?.ShowFameNotification(OwnerClientId, amount);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CREDITS
    // ═════════════════════════════════════════════════════════════════════════

    public void AddCredits(int amount)
    {
        if (!IsServer) return;
        credits.Value += amount;
    }

    /// <summary>
    /// Attempts to deduct credits. Returns false and does nothing if insufficient.
    /// </summary>
    public bool SpendCredits(int amount)
    {
        if (!IsServer)              return false;
        if (credits.Value < amount) return false;
        credits.Value -= amount;
        return true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HEALTH
    // ═════════════════════════════════════════════════════════════════════════

    public void TakeScoundrelDamage(int amount)
    {
        if (!IsServer) return;
        scoundrelHealth.Value = Mathf.Max(0, scoundrelHealth.Value - amount);
        if (scoundrelHealth.Value <= 0) HandleScoundrelDefeated();
    }

    public void TakeHullDamage(int amount)
    {
        if (!IsServer) return;
        shipHullHealth.Value = Mathf.Max(0, shipHullHealth.Value - amount);
        if (shipHullHealth.Value <= 0) HandleShipDestroyed();
    }

    /// <summary>Planning Phase: Heal — restores all Scoundrel and Hull health.</summary>
    public void HealAll()
    {
        if (!IsServer) return;
        scoundrelHealth.Value = Stats.MaxHealth;
        shipHullHealth.Value  = Ship.MaxHullHealth;
    }

    private void HandleScoundrelDefeated()
    {
        // Penalty: lose half credits, respawn at nearest safe node.
        int penalty = credits.Value / 2;
        SpendCredits(penalty);
        scoundrelHealth.Value = Stats.MaxHealth; // Reset health after defeat
        // TODO: Teleport to start node, notify clients
        ScoundrelDefeatedClientRpc();
    }

    private void HandleShipDestroyed()
    {
        shipHullHealth.Value = Ship.MaxHullHealth;
        ShipDestroyedClientRpc();
    }

    [ClientRpc] private void ScoundrelDefeatedClientRpc() { UIManager.Instance?.ShowDefeatedScreen(OwnerClientId); }
    [ClientRpc] private void ShipDestroyedClientRpc()     { UIManager.Instance?.ShowShipDestroyedScreen(OwnerClientId); }

    // ═════════════════════════════════════════════════════════════════════════
    // MOVEMENT
    // ═════════════════════════════════════════════════════════════════════════

    public void MoveToNode(int nodeId)
    {
        if (!IsServer) return;
        currentNodeId.Value = nodeId;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // REPUTATION
    // ═════════════════════════════════════════════════════════════════════════

    public int GetReputation(FactionType faction)
    {
        return faction switch
        {
            FactionType.Syndicate => repSyndicate.Value,
            FactionType.Authority => repAuthority.Value,
            FactionType.Rebels    => repRebels.Value,
            FactionType.Hutts     => repHutts.Value,
            _                     => 0
        };
    }

    public ReputationStatus GetReputationStatus(FactionType faction)
    {
        int rep = GetReputation(faction);
        if (rep < 0) return ReputationStatus.Negative;
        if (rep > 0) return ReputationStatus.Positive;
        return ReputationStatus.Neutral;
    }

    public void ModifyReputation(FactionType faction, int delta)
    {
        if (!IsServer) return;
        int current = GetReputation(faction);
        int clamped = Mathf.Clamp(current + delta, -3, 3);

        switch (faction)
        {
            case FactionType.Syndicate: repSyndicate.Value = clamped; break;
            case FactionType.Authority: repAuthority.Value = clamped; break;
            case FactionType.Rebels:    repRebels.Value    = clamped; break;
            case FactionType.Hutts:     repHutts.Value     = clamped; break;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // INVENTORY
    // ═════════════════════════════════════════════════════════════════════════

    public bool AddCargo(CardInstanceData card)
    {
        if (!IsServer) return false;
        if (cargoHold.Count >= Ship.CargoSlots) return false;
        cargoHold.Add(card);
        return true;
    }

    public bool AddCrewMember(CardInstanceData card)
    {
        if (!IsServer) return false;
        if (crewMembers.Count >= Ship.CrewSlots) return false;
        crewMembers.Add(card);
        return true;
    }

    public bool AddMod(CardInstanceData card)
    {
        if (!IsServer) return false;
        if (shipMods.Count >= Ship.ModSlots) return false;
        shipMods.Add(card);
        return true;
    }

    public void AddGear(CardInstanceData card)    { if (IsServer) gearItems.Add(card); }
    public void AddJob(CardInstanceData card)     { if (IsServer) activeJobs.Add(card); }
    public void AddBounty(CardInstanceData card)  { if (IsServer) activeBounties.Add(card); }

    public bool RemoveCargo(CardInstanceData card)  { return IsServer && cargoHold.Remove(card); }
    public bool RemoveJob(CardInstanceData card)    { return IsServer && activeJobs.Remove(card); }
    public bool RemoveBounty(CardInstanceData card) { return IsServer && activeBounties.Remove(card); }

    // ═════════════════════════════════════════════════════════════════════════
    // COMPUTED STATS (account for equipped mods, crew bonuses, etc.)
    // ═════════════════════════════════════════════════════════════════════════

    public int GetEffectiveSpeed()
    {
        int speed = Ship.Speed;
        // TODO: Iterate shipMods for SpeedBonus values and sum them.
        return speed;
    }

    public int GetSkillValue(SkillType skill)
    {
        int baseVal = skill switch
        {
            SkillType.Combat   => Stats.Combat,
            SkillType.Piloting => Stats.Piloting,
            SkillType.Cunning  => Stats.Cunning,
            SkillType.Tech     => Stats.Tech,
            _                  => 0
        };
        // TODO: Add bonuses from gearItems and crewMembers.
        return baseVal;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CHARACTER ASSIGNMENT (called before/at game start)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Server assigns character and ship data by index into a shared SO registry.
    /// Synced to clients via a ClientRpc that loads the same SO by index.
    /// </summary>
    public void AssignCharacter(int scoundrelIndex, int shipIndex)
    {
        if (!IsServer) return;
        AssignCharacterClientRpc(scoundrelIndex, shipIndex);
    }

    [ClientRpc]
    private void AssignCharacterClientRpc(int scoundrelIndex, int shipIndex)
    {
        scoundrelData = CharacterRegistry.Instance?.GetScoundrel(scoundrelIndex);
        shipData      = CharacterRegistry.Instance?.GetShip(shipIndex);
        LoadStatsFromScriptableObjects();
    }
}

// ─── Stat Struct Wrappers ────────────────────────────────────────────────────

[System.Serializable]
public struct ScoundrelStats
{
    public string CharacterName;
    public string PersonalGoal;
    public int    MaxHealth;
    public int    Combat;
    public int    Piloting;
    public int    Cunning;
    public int    Tech;

    public ScoundrelStats(ScoundrelData d)
    {
        CharacterName = d.CharacterName;
        PersonalGoal  = d.PersonalGoal;
        MaxHealth     = d.MaxHealth;
        Combat        = d.CombatSkill;
        Piloting      = d.PilotingSkill;
        Cunning       = d.CunningSkill;
        Tech          = d.TechSkill;
    }

    public static ScoundrelStats Default() => new ScoundrelStats
        { CharacterName = "Unknown", MaxHealth = 3, Combat = 2, Piloting = 2, Cunning = 2, Tech = 2 };
}

[System.Serializable]
public struct ShipStats
{
    public string ShipName;
    public int    Speed;
    public int    MaxHullHealth;
    public int    CargoSlots;
    public int    CrewSlots;
    public int    ModSlots;
    public int    AttackDice;

    public ShipStats(ShipData d)
    {
        ShipName      = d.ShipName;
        Speed         = d.Speed;
        MaxHullHealth = d.MaxHullHealth;
        CargoSlots    = d.CargoSlots;
        CrewSlots     = d.CrewSlots;
        ModSlots      = d.ModSlots;
        AttackDice    = d.AttackDice;
    }

    public static ShipStats Default() => new ShipStats
        { ShipName = "Unknown Ship", Speed = 2, MaxHullHealth = 4, CargoSlots = 2, CrewSlots = 1, ModSlots = 1, AttackDice = 1 };
}
```

---

## 4. Node Movement & Pathfinding

The map is a weighted graph where **each edge costs 1 move**. Ship speed defines how many 
edges can be traversed per Planning Phase. BFS (Breadth-First Search) is ideal here because:

- All edges have equal cost (no weighted paths needed).
- We need to find **all reachable nodes** (for highlighting), not just one path.
- Maps are small (20–50 nodes), so performance is not a concern.

### 4.1 `MapNode.cs`

```csharp
// MapNode.cs
// Represents a single location on the board: either a Planet or a NavPoint.
// Assigned a unique integer ID in the Inspector.

using System.Collections.Generic;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    // ─── Identity ─────────────────────────────────────────────────────────────
    [Header("Node Identity")]
    public int         NodeId;          // Unique. Assign sequentially in Inspector.
    public string      NodeName;
    public MapNodeType NodeType;

    [Header("Planet-Specific")]
    public string      PlanetId;        // Matches keys in encounter card decks.
    public FactionType FactionOwner = FactionType.None;

    [Header("Hyperspace Connections")]
    public List<MapNode> ConnectedNodes = new List<MapNode>();

    [Header("Contact Token")]
    public bool             HasContactToken;
    public ContactTokenData ContactToken;   // SO: what happens when encountered

    // ─── Runtime State ─────────────────────────────────────────────────────────
    private readonly List<ulong> occupyingClientIds = new List<ulong>();

    public void AddPlayerToNode(ulong clientId)
    {
        if (!occupyingClientIds.Contains(clientId))
            occupyingClientIds.Add(clientId);
        RefreshVisuals();
    }

    public void RemovePlayerFromNode(ulong clientId)
    {
        occupyingClientIds.Remove(clientId);
        RefreshVisuals();
    }

    public IReadOnlyList<ulong> GetOccupants() => occupyingClientIds.AsReadOnly();

    public bool IsOccupied => occupyingClientIds.Count > 0;

    private void RefreshVisuals()
    {
        GetComponent<MapNodeVisuals>()?.UpdateOccupantDisplay(occupyingClientIds.Count);
    }

    // ─── Editor Gizmo ─────────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = NodeType == MapNodeType.Planet ? Color.yellow : Color.cyan;
        foreach (var neighbor in ConnectedNodes)
        {
            if (neighbor != null)
                Gizmos.DrawLine(transform.position, neighbor.transform.position);
        }
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}
```

### 4.2 `MapManager.cs` — BFS Pathfinding

```csharp
// MapManager.cs
// Central registry for all MapNodes. Provides:
//   1. GetReachableNodes()  — all nodes within N moves (for UI highlight)
//   2. FindPath()           — shortest route to a specific node (for validation + animation)
//   3. Visual helpers       — highlight reachable nodes, animate ship along path

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    public static MapManager Instance { get; private set; }

    [Header("Scene Nodes")]
    [SerializeField] private List<MapNode> allNodes = new List<MapNode>();

    [Header("Ship Animation")]
    [SerializeField] private float shipMoveSpeed = 4f; // Units per second

    // Fast lookup: NodeId → MapNode
    private Dictionary<int, MapNode> nodeRegistry;

    // Runtime ship GameObjects per player (clientId → ship GameObject)
    private Dictionary<ulong, GameObject> playerShips = new Dictionary<ulong, GameObject>();

    // ═════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        BuildRegistry();
    }

    private void BuildRegistry()
    {
        nodeRegistry = new Dictionary<int, MapNode>(allNodes.Count);
        foreach (var node in allNodes)
        {
            if (node == null) continue;
            if (nodeRegistry.ContainsKey(node.NodeId))
                Debug.LogError($"[MapManager] Duplicate NodeId {node.NodeId} on '{node.NodeName}'");
            else
                nodeRegistry[node.NodeId] = node;
        }
        Debug.Log($"[MapManager] Registered {nodeRegistry.Count} nodes.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PATHFINDING — BFS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns all MapNodes reachable from <paramref name="startId"/> within
    /// <paramref name="speed"/> moves, using Breadth-First Search.
    /// Does NOT include the start node itself.
    ///
    /// Time complexity: O(V + E), where V = nodes, E = edges.
    /// </summary>
    public HashSet<MapNode> GetReachableNodes(int startId, int speed)
    {
        if (!nodeRegistry.TryGetValue(startId, out MapNode startNode))
            return new HashSet<MapNode>();

        var reachable = new HashSet<MapNode>();

        // Each queue entry: (node, stepsUsed)
        var queue   = new Queue<(MapNode node, int steps)>();
        var visited = new Dictionary<MapNode, int>(); // node → min steps to reach

        queue.Enqueue((startNode, 0));
        visited[startNode] = 0;

        while (queue.Count > 0)
        {
            var (current, steps) = queue.Dequeue();

            if (steps > 0)           // Don't include origin
                reachable.Add(current);

            if (steps >= speed)      // Cannot move further
                continue;

            foreach (var neighbor in current.ConnectedNodes)
            {
                if (neighbor == null) continue;
                int newSteps = steps + 1;

                // Visit if not seen, or if we found a shorter path to this node.
                if (!visited.TryGetValue(neighbor, out int prevSteps) || newSteps < prevSteps)
                {
                    visited[neighbor] = newSteps;
                    queue.Enqueue((neighbor, newSteps));
                }
            }
        }

        return reachable;
    }

    /// <summary>
    /// Finds the shortest path from <paramref name="startId"/> to
    /// <paramref name="destId"/>, constrained to at most <paramref name="maxSpeed"/> moves.
    ///
    /// Returns an ordered List of NodeIds (excluding start, including destination).
    /// Returns null if destination is unreachable within the speed constraint.
    /// </summary>
    public List<int> FindPath(int startId, int destId, int maxSpeed)
    {
        if (startId == destId) return new List<int>(); // Already there

        if (!nodeRegistry.TryGetValue(startId, out MapNode startNode) ||
            !nodeRegistry.TryGetValue(destId,   out MapNode destNode))
            return null;

        // BFS with parent tracking for path reconstruction.
        var parent   = new Dictionary<MapNode, MapNode> { [startNode] = null };
        var queue    = new Queue<(MapNode node, int steps)>();
        bool found   = false;

        queue.Enqueue((startNode, 0));

        while (queue.Count > 0 && !found)
        {
            var (current, steps) = queue.Dequeue();

            if (current == destNode) { found = true; break; }
            if (steps >= maxSpeed)   continue;

            foreach (var neighbor in current.ConnectedNodes)
            {
                if (neighbor == null || parent.ContainsKey(neighbor)) continue;
                parent[neighbor] = current;
                queue.Enqueue((neighbor, steps + 1));
            }
        }

        if (!found) return null; // Destination unreachable within speed

        // Reconstruct path from destination back to start.
        var path = new List<int>();
        MapNode step = destNode;
        while (step != startNode)
        {
            path.Add(step.NodeId);
            step = parent[step];
        }
        path.Reverse(); // Start-to-destination order
        return path;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // VISUAL MOVEMENT MODE (client-side)
    // ═════════════════════════════════════════════════════════════════════════

    private HashSet<MapNode> currentHighlightedNodes = new HashSet<MapNode>();

    /// <summary>
    /// Called on the active client when movement mode begins.
    /// Highlights valid destination nodes and enables click-to-move.
    /// </summary>
    public void ShowReachableNodes(int startId, int speed)
    {
        ClearMovementHighlights();
        currentHighlightedNodes = GetReachableNodes(startId, speed);

        foreach (var node in currentHighlightedNodes)
        {
            var visuals = node.GetComponent<MapNodeVisuals>();
            if (visuals == null) continue;

            visuals.SetHighlighted(true);
            visuals.SetClickCallback(() => OnNodeClicked(node.NodeId));
        }
    }

    private void OnNodeClicked(int destinationId)
    {
        ClearMovementHighlights();
        // Send move request to server.
        GameManager.Instance?.ConfirmShipMovementServerRpc(destinationId);
    }

    public void ClearMovementHighlights()
    {
        foreach (var node in currentHighlightedNodes)
        {
            var visuals = node.GetComponent<MapNodeVisuals>();
            if (visuals != null)
            {
                visuals.SetHighlighted(false);
                visuals.ClearClickCallback();
            }
        }
        currentHighlightedNodes.Clear();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SHIP ANIMATION (all clients, called via ClientRpc)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Smoothly moves a player's ship GameObject along the given node path.
    /// Called on ALL clients simultaneously from a ClientRpc.
    /// </summary>
    public void AnimateShipAlongPath(ulong clientId, int[] pathNodeIds)
    {
        if (!playerShips.TryGetValue(clientId, out GameObject ship)) return;

        var nodes = pathNodeIds
            .Select(id => nodeRegistry.TryGetValue(id, out MapNode n) ? n : null)
            .Where(n => n != null)
            .ToList();

        StartCoroutine(ShipMoveCoroutine(ship, nodes));
    }

    private IEnumerator ShipMoveCoroutine(GameObject ship, List<MapNode> waypoints)
    {
        foreach (var waypoint in waypoints)
        {
            Vector3 destination = waypoint.transform.position;

            while (Vector3.Distance(ship.transform.position, destination) > 0.02f)
            {
                ship.transform.position = Vector3.MoveTowards(
                    ship.transform.position, destination, shipMoveSpeed * Time.deltaTime);
                // Optional: rotate ship toward destination
                ship.transform.forward = Vector3.Lerp(
                    ship.transform.forward,
                    (destination - ship.transform.position).normalized,
                    Time.deltaTime * 8f);
                yield return null;
            }

            ship.transform.position = destination;
            yield return new WaitForSeconds(0.05f); // Brief pause per waypoint
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    public MapNode GetNodeById(int id) =>
        nodeRegistry.TryGetValue(id, out MapNode node) ? node : null;

    public void RegisterPlayerShip(ulong clientId, GameObject shipObject) =>
        playerShips[clientId] = shipObject;

    public List<MapNode> GetAllNodes() => new List<MapNode>(allNodes);
}
```

**Pathfinding Decision Table:**

| Scenario | Method | Return |
|---|---|---|
| Highlight valid destinations | `GetReachableNodes(startId, speed)` | `HashSet<MapNode>` |
| Validate player's chosen move | `FindPath(start, dest, speed) != null` | `bool` |
| Animate ship along route | `FindPath(start, dest, speed)` → `AnimateShipAlongPath` | `List<int>` |
| Check if two nodes are adjacent | `node.ConnectedNodes.Contains(other)` | `bool` |

---

## 5. Market Deck System

### 5.1 Architecture Overview

```
ScriptableObjects (content/data layer)     Runtime (logic layer)
───────────────────────────────────────    ─────────────────────
CardData (base SO)                         MarketDeck (plain class)
  ├── BountyCardData                  →      drawPile : List<CardData>
  ├── CargoCardData                          discardPile : List<CardData>
  ├── GearCardData                          marketRow : List<CardData>
  ├── ModCardData                       ↑
  ├── JobCardData                    DeckManager : MonoBehaviour
  └── LuxuryCardData                    - 6 MarketDeck instances
                                         - 1 dict of planet encounter decks
                                         - ServerRpcs for buy/cycle actions
```

The SO hierarchy means artists and designers can create/edit cards entirely in the Unity 
Inspector without touching code. The JSON pipeline provides an alternative for rapid 
iteration or if cards are data-driven from an external tool.

### 5.2 `CardData.cs` — ScriptableObject Hierarchy

```csharp
// CardData.cs — Base ScriptableObject for all market cards.

using UnityEngine;

[CreateAssetMenu(fileName = "NewCard", menuName = "OuterRim/Cards/Base Card")]
public class CardData : ScriptableObject
{
    [Header("Identity")]
    public int          CardId;       // Must be unique across ALL card types for NetworkList sync.
    public string       CardName;
    [TextArea(2, 4)]
    public string       FlavorText;
    public Sprite       CardArt;
    public MarketDeckType DeckType;

    [Header("Market")]
    public int BuyCost;
    public int SellValue;           // Credits gained when discarding/selling

    [Header("Effect")]
    [TextArea(2, 5)]
    public string EffectDescription; // Human-readable for UI tooltip
}

// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "NewBounty", menuName = "OuterRim/Cards/Bounty")]
public class BountyCardData : CardData
{
    [Header("Bounty Details")]
    public string      TargetName;
    public string      DeliveryPlanetId;   // Which planet to bring the bounty to
    public FactionType IssuingFaction;
    public int         RewardCredits;
    public int         RewardFame;
    public bool        RequiresPiloting;   // True = needs a Piloting check to capture
    public int         PilotingDifficulty;
}

// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "NewCargo", menuName = "OuterRim/Cards/Cargo")]
public class CargoCardData : CardData
{
    [Header("Cargo Details")]
    public string DeliveryPlanetId;
    public int    DeliveryReward;
    public bool   IsIllegal;           // If true: triggers rep check at Authority planets
    public int    IllegalFameReward;   // Extra fame if delivered to black market
}

// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "NewGear", menuName = "OuterRim/Cards/Gear")]
public class GearCardData : CardData
{
    [Header("Gear Stats")]
    public SkillType AffectedSkill;
    public int       SkillBonus;
    public bool      IsSingleUse;          // Discard after use
    public string    ActivationCondition;  // For UI tooltip (e.g., "When rolling Combat")
}

// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "NewMod", menuName = "OuterRim/Cards/Mod")]
public class ModCardData : CardData
{
    [Header("Ship Mod Stats")]
    public int SpeedBonus;
    public int HullBonus;
    public int ExtraCargoSlots;
    public int ExtraCrewSlots;
    public int AttackDiceBonus;
    public string SpecialRule;
}

// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "NewJob", menuName = "OuterRim/Cards/Job")]
public class JobCardData : CardData
{
    [Header("Job Details")]
    public string OriginPlanetId;       // Where the job starts (can be anywhere)
    public string DestinationPlanetId;  // Where to deliver
    public int    CreditAdvance;        // Credits given immediately when job is taken
    public int    RewardCredits;        // Credits on delivery
    public int    RewardFame;
    public bool   RequiresCunning;      // Needs a Cunning check at destination
    public int    CunningDifficulty;
}

// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "NewLuxury", menuName = "OuterRim/Cards/Luxury")]
public class LuxuryCardData : CardData
{
    [Header("Luxury Details")]
    public string PreferredBuyPlanetId;     // Where it's cheapest (or found free)
    public string PremiumSellPlanetId;      // Where it sells for the most
    public int    PremiumSellValue;
    public bool   IsRestrictedItem;         // Illegal to carry through certain faction space
}

// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "NewEncounter", menuName = "OuterRim/Cards/Planet Encounter")]
public class EncounterCardData : CardData
{
    [Header("Encounter")]
    public string      PlanetId;           // Which planet deck this belongs to
    [TextArea(3, 6)]
    public string      EncounterText;      // Narrative text shown to player

    [Header("Skill Check")]
    public bool        RequiresCheck;
    public SkillType   CheckSkill;
    public int         CheckDifficulty;    // Minimum hits needed

    [Header("Success Outcome")]
    public int         SuccessCredits;
    public int         SuccessFame;
    public int         SuccessRepDelta;
    public FactionType SuccessRepFaction;

    [Header("Failure Outcome")]
    public int         FailureDamage;
    public int         FailureCreditsLost;
    public int         FailureRepDelta;
    public FactionType FailureRepFaction;
}
```

### 5.3 `MarketDeck.cs` — Runtime Deck Logic

```csharp
// MarketDeck.cs
// Plain C# class (not MonoBehaviour). One instance per market deck type.
// Managed entirely by DeckManager on the server.

using System.Collections.Generic;
using UnityEngine;

public class MarketDeck
{
    public MarketDeckType DeckType      { get; private set; }
    public int            MarketRowSize { get; private set; }

    private List<CardData> drawPile    = new List<CardData>();
    private List<CardData> discardPile = new List<CardData>();
    private List<CardData> marketRow   = new List<CardData>(); // Visible to all players

    public int                    DrawCount    => drawPile.Count;
    public int                    DiscardCount => discardPile.Count;
    public IReadOnlyList<CardData> MarketRow   => marketRow.AsReadOnly();

    public MarketDeck(MarketDeckType type, int rowSize = 3)
    {
        DeckType      = type;
        MarketRowSize = rowSize;
    }

    // ─── Initialization ───────────────────────────────────────────────────────

    public void Initialize(List<CardData> cards)
    {
        drawPile = new List<CardData>(cards);
        Shuffle();
        RefillMarketRow();
    }

    private void Shuffle()
    {
        for (int i = drawPile.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (drawPile[i], drawPile[j]) = (drawPile[j], drawPile[i]);
        }
    }

    // ─── Draw ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the top card from the draw pile.
    /// If draw pile is empty, shuffles discard pile back in.
    /// Returns null if both piles are empty.
    /// </summary>
    public CardData DrawCard()
    {
        if (drawPile.Count == 0)
        {
            if (discardPile.Count == 0) return null;
            drawPile = new List<CardData>(discardPile);
            discardPile.Clear();
            Shuffle();
            Debug.Log($"[MarketDeck:{DeckType}] Reshuffled discard into draw pile.");
        }

        var card = drawPile[0];
        drawPile.RemoveAt(0);
        return card;
    }

    public void Discard(CardData card)
    {
        if (card != null) discardPile.Add(card);
    }

    // ─── Market Row Operations ────────────────────────────────────────────────

    /// <summary>
    /// Player purchases card at <paramref name="rowIndex"/> from the market row.
    /// The slot is immediately refilled from the draw pile.
    /// Returns the purchased card, or null if index is invalid.
    /// </summary>
    public CardData PurchaseFromMarket(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= marketRow.Count) return null;
        var card = marketRow[rowIndex];
        marketRow.RemoveAt(rowIndex);
        RefillMarketRow();
        return card;
    }

    /// <summary>
    /// "Cycle" action: pay a fee to discard the chosen market card and replace it.
    /// Returns the new card now in that slot, or null on failure.
    /// </summary>
    public CardData CycleMarketCard(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= marketRow.Count) return null;
        var old = marketRow[rowIndex];
        Discard(old);
        var newCard = DrawCard();
        if (newCard != null) marketRow[rowIndex] = newCard;
        else marketRow.RemoveAt(rowIndex); // Deck fully exhausted
        return newCard;
    }

    private void RefillMarketRow()
    {
        while (marketRow.Count < MarketRowSize)
        {
            var card = DrawCard();
            if (card == null) break; // All cards exhausted
            marketRow.Add(card);
        }
    }
}
```

### 5.4 `DeckManager.cs`

```csharp
// DeckManager.cs
// Initializes, owns, and exposes all market decks + planet encounter decks.
// Deck contents are NEVER sent to clients directly (anti-cheat).
// Only the Market Row is revealed, via ClientRpcs.

using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class DeckManager : NetworkBehaviour
{
    public static DeckManager Instance { get; private set; }

    // ─── SO Card Lists (populate in Inspector) ─────────────────────────────
    [Header("Market Deck Sources — ScriptableObjects")]
    [SerializeField] private List<BountyCardData>  bountyCards;
    [SerializeField] private List<CargoCardData>   cargoCards;
    [SerializeField] private List<GearCardData>    gearCards;
    [SerializeField] private List<ModCardData>     modCards;
    [SerializeField] private List<JobCardData>     jobCards;
    [SerializeField] private List<LuxuryCardData>  luxuryCards;
    [SerializeField] private List<EncounterCardData> encounterCards;

    [Header("JSON Alternative")]
    [SerializeField] private bool       useJsonPipeline;
    [SerializeField] private TextAsset  cardDatabaseJson;

    [Header("Market Row Size")]
    [SerializeField] private int defaultMarketRowSize = 3;
    [SerializeField] private int cycleCost = 200; // Credits to cycle one card

    // ─── Runtime Decks ─────────────────────────────────────────────────────
    private Dictionary<MarketDeckType, MarketDeck> marketDecks;
    private Dictionary<string, Queue<EncounterCardData>> planetEncounterDecks; // planetId → card queue

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        InitializeDecks();
    }

    // ─── Initialization ────────────────────────────────────────────────────

    private void InitializeDecks()
    {
        marketDecks = new Dictionary<MarketDeckType, MarketDeck>();

        if (useJsonPipeline && cardDatabaseJson != null)
            LoadFromJson();
        else
            LoadFromScriptableObjects();

        BuildPlanetEncounterDecks();
        BroadcastAllMarketRowsClientRpc(BuildMarketRowPayload());

        Debug.Log("[DeckManager] All decks initialized.");
    }

    private void LoadFromScriptableObjects()
    {
        marketDecks[MarketDeckType.Bounties] = CreateAndInit(MarketDeckType.Bounties, bountyCards.Cast<CardData>().ToList());
        marketDecks[MarketDeckType.Cargo]    = CreateAndInit(MarketDeckType.Cargo,    cargoCards.Cast<CardData>().ToList());
        marketDecks[MarketDeckType.Gear]     = CreateAndInit(MarketDeckType.Gear,     gearCards.Cast<CardData>().ToList());
        marketDecks[MarketDeckType.Mods]     = CreateAndInit(MarketDeckType.Mods,     modCards.Cast<CardData>().ToList());
        marketDecks[MarketDeckType.Jobs]     = CreateAndInit(MarketDeckType.Jobs,     jobCards.Cast<CardData>().ToList());
        marketDecks[MarketDeckType.Luxury]   = CreateAndInit(MarketDeckType.Luxury,   luxuryCards.Cast<CardData>().ToList());
    }

    private MarketDeck CreateAndInit(MarketDeckType type, List<CardData> cards)
    {
        var deck = new MarketDeck(type, defaultMarketRowSize);
        deck.Initialize(cards);
        return deck;
    }

    private void BuildPlanetEncounterDecks()
    {
        planetEncounterDecks = new Dictionary<string, Queue<EncounterCardData>>();

        // Group encounter cards by their PlanetId, shuffle each group.
        var grouped = encounterCards
            .Where(c => c != null && !string.IsNullOrEmpty(c.PlanetId))
            .GroupBy(c => c.PlanetId);

        foreach (var group in grouped)
        {
            var shuffled = group.ToList();
            shuffled.Shuffle();
            planetEncounterDecks[group.Key] = new Queue<EncounterCardData>(shuffled);
        }
    }

    // ─── JSON Pipeline ─────────────────────────────────────────────────────

    private void LoadFromJson()
    {
        var db = JsonUtility.FromJson<CardDatabase>(cardDatabaseJson.text);
        if (db == null) { Debug.LogError("[DeckManager] Failed to parse card JSON."); return; }

        // For each market deck type, find matching JSON records and create runtime CardData.
        // (Runtime CardData are created as plain C# objects, not SOs, in this pipeline.)
        // See Section 5.5 for JSON schema.
        // TODO: Map JsonCardRecord → CardData subclass instances for each deck.
        Debug.LogWarning("[DeckManager] JSON pipeline not fully implemented. See TDD Section 5.5.");
    }

    // ─── Market Row Broadcasting ───────────────────────────────────────────

    private MarketRowPayload BuildMarketRowPayload()
    {
        // Pack all 6 market rows into a single payload to minimize RPCs at startup.
        var payload = new MarketRowPayload();
        foreach (var kvp in marketDecks)
            payload.Rows.Add(new MarketRowEntry
            {
                DeckType = kvp.Key,
                CardIds  = kvp.Value.MarketRow.Select(c => c.CardId).ToArray()
            });
        return payload;
    }

    [ClientRpc]
    private void BroadcastAllMarketRowsClientRpc(MarketRowPayload payload)
    {
        UIManager.Instance?.RefreshAllMarketRows(payload);
    }

    // ─── Public Actions (called from ServerRpcs) ───────────────────────────

    public MarketDeck GetDeck(MarketDeckType type) =>
        marketDecks.TryGetValue(type, out MarketDeck deck) ? deck : null;

    /// <summary>
    /// Attempts to purchase a card from the market row. Deducts cost from player.
    /// Returns the card on success, null on failure.
    /// </summary>
    public CardData TryPurchaseCard(PlayerState buyer, MarketDeckType deckType, int rowIndex)
    {
        if (!IsServer) return null;
        if (!marketDecks.TryGetValue(deckType, out MarketDeck deck)) return null;

        var card = deck.MarketRow.ElementAtOrDefault(rowIndex);
        if (card == null) return null;
        if (!buyer.SpendCredits(card.BuyCost)) return null; // Insufficient funds

        deck.PurchaseFromMarket(rowIndex);
        NotifyMarketRowUpdateClientRpc(deckType, deck.MarketRow.Select(c => c.CardId).ToArray());
        return card;
    }

    /// <summary>
    /// Player pays the cycle fee to replace a market card.
    /// </summary>
    public CardData TryCycleCard(PlayerState player, MarketDeckType deckType, int rowIndex)
    {
        if (!IsServer) return null;
        if (!player.SpendCredits(cycleCost)) return null;
        if (!marketDecks.TryGetValue(deckType, out MarketDeck deck)) return null;

        var newCard = deck.CycleMarketCard(rowIndex);
        NotifyMarketRowUpdateClientRpc(deckType, deck.MarketRow.Select(c => c.CardId).ToArray());
        return newCard;
    }

    [ClientRpc]
    private void NotifyMarketRowUpdateClientRpc(MarketDeckType deckType, int[] cardIds)
    {
        UIManager.Instance?.RefreshMarketRow(deckType, cardIds);
    }

    /// <summary>
    /// Draws an encounter card for the given planet. Reshuffles automatically when exhausted.
    /// </summary>
    public EncounterCardData DrawPlanetEncounterCard(string planetId)
    {
        if (!IsServer) return null;
        if (!planetEncounterDecks.TryGetValue(planetId, out var queue)) return null;

        if (queue.Count == 0)
        {
            Debug.LogWarning($"[DeckManager] Encounter deck for planet '{planetId}' exhausted. Reshuffling.");
            var allForPlanet = encounterCards.Where(c => c.PlanetId == planetId).ToList();
            allForPlanet.Shuffle();
            foreach (var c in allForPlanet) queue.Enqueue(c);
        }

        return queue.Count > 0 ? queue.Dequeue() : null;
    }
}

// ─── Market Row Payload Structs ──────────────────────────────────────────────

[System.Serializable]
public class MarketRowPayload : INetworkSerializable
{
    public List<MarketRowEntry> Rows = new List<MarketRowEntry>();

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        int count = Rows.Count;
        s.SerializeValue(ref count);
        if (s.IsReader) Rows = new List<MarketRowEntry>(count);
        for (int i = 0; i < count; i++)
        {
            var entry = i < Rows.Count ? Rows[i] : new MarketRowEntry();
            entry.NetworkSerialize(s);
            if (i < Rows.Count) Rows[i] = entry;
            else Rows.Add(entry);
        }
    }
}

[System.Serializable]
public struct MarketRowEntry : INetworkSerializable
{
    public MarketDeckType DeckType;
    public int[]          CardIds;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        int deckInt = (int)DeckType;
        s.SerializeValue(ref deckInt);
        DeckType = (MarketDeckType)deckInt;

        int len = CardIds?.Length ?? 0;
        s.SerializeValue(ref len);
        if (s.IsReader) CardIds = new int[len];
        for (int i = 0; i < len; i++) s.SerializeValue(ref CardIds[i]);
    }
}
```

### 5.5 JSON Schema (Alternative Pipeline)

```json
{
  "cards": [
    {
      "id": 1001,
      "name": "Spice Shipment",
      "deckType": "Cargo",
      "buyCost": 0,
      "sellValue": 200,
      "flavorText": "Handle with extreme caution.",
      "effectDescription": "Deliver to Kessel for 1500 credits.",
      "cargo": {
        "deliveryPlanetId": "kessel",
        "deliveryReward": 1500,
        "isIllegal": true,
        "illegalFameReward": 0
      }
    },
    {
      "id": 2001,
      "name": "Elite Mercenary",
      "deckType": "Gear",
      "buyCost": 900,
      "sellValue": 300,
      "flavorText": "Doesn't ask questions.",
      "effectDescription": "+2 Combat dice.",
      "gear": {
        "affectedSkill": "Combat",
        "skillBonus": 2,
        "isSingleUse": false
      }
    },
    {
      "id": 3001,
      "name": "Afterburner",
      "deckType": "Mods",
      "buyCost": 1200,
      "sellValue": 400,
      "flavorText": "She's got it where it counts.",
      "effectDescription": "+1 Ship Speed.",
      "mod": {
        "speedBonus": 1,
        "hullBonus": 0,
        "extraCargoSlots": 0,
        "attackDiceBonus": 0
      }
    }
  ]
}
```

---

## 6. Supporting Systems

### 6.1 Dice System

```csharp
// DiceFaceDistribution.cs
// ScriptableObject defining the probability weights for one type of 8-sided die.
// Default: 2 Blank, 2 Focus, 3 Hit, 1 Crit (sums to 8 faces).

using UnityEngine;

[CreateAssetMenu(fileName = "StandardDie", menuName = "OuterRim/Dice/Face Distribution")]
public class DiceFaceDistribution : ScriptableObject
{
    [Range(0, 8)] public int BlankFaces = 2;
    [Range(0, 8)] public int FocusFaces = 2;
    [Range(0, 8)] public int HitFaces   = 3;
    [Range(0, 8)] public int CritFaces  = 1;

    public DieFace RollOneDie()
    {
        int total = BlankFaces + FocusFaces + HitFaces + CritFaces;
        if (total <= 0) return DieFace.Blank;

        int roll = Random.Range(0, total);

        if (roll < BlankFaces)                              return DieFace.Blank;
        if (roll < BlankFaces + FocusFaces)                return DieFace.Focus;
        if (roll < BlankFaces + FocusFaces + HitFaces)     return DieFace.Hit;
        return DieFace.Crit;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

// DiceRoller.cs — Static utility. All rolls happen server-side.

using System.Collections.Generic;
using UnityEngine;

public readonly struct DiceRollResult
{
    public readonly List<DieFace> Faces;

    public DiceRollResult(List<DieFace> faces) => Faces = faces;

    // Crits count as Hits AND crits.
    public int Hits    => Faces.Count(f => f == DieFace.Hit || f == DieFace.Crit);
    public int Crits   => Faces.Count(f => f == DieFace.Crit);
    public int Focuses => Faces.Count(f => f == DieFace.Focus);
    public int Blanks  => Faces.Count(f => f == DieFace.Blank);

    public override string ToString() =>
        $"[Hits:{Hits} Crits:{Crits} Focus:{Focuses} Blank:{Blanks}]";
}

public static class DiceRoller
{
    /// <summary>
    /// Rolls <paramref name="numDice"/> dice using the given distribution.
    /// All rolls must be performed on the server to prevent cheating.
    /// </summary>
    public static DiceRollResult Roll(int numDice, DiceFaceDistribution distribution)
    {
        numDice = Mathf.Max(1, numDice); // Always roll at least 1
        var faces = new List<DieFace>(numDice);
        for (int i = 0; i < numDice; i++)
            faces.Add(distribution.RollOneDie());
        return new DiceRollResult(faces);
    }
}
```

### 6.2 Combat Resolver

```csharp
// CombatResolver.cs
// Handles all contested dice rolls (player vs patrol, skill checks).
// Runs exclusively on the server; results broadcast via ClientRpc.

using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class CombatResolver : NetworkBehaviour
{
    public static CombatResolver Instance { get; private set; }

    [SerializeField] private DiceFaceDistribution standardDie;

    public struct CombatOutcome
    {
        public bool          PlayerWon;
        public int           DamageToEnemy;   // Hull damage if player wins
        public int           DamageToPlayer;  // Scoundrel damage if enemy wins
        public DiceRollResult PlayerRoll;
        public DiceRollResult EnemyRoll;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ─── Ship Combat ──────────────────────────────────────────────────────────

    /// <summary>
    /// Full combat sequence: rolls dice for both sides, applies damage,
    /// broadcasts the visual result to all clients.
    /// Crits deal +1 bonus damage on top of the margin of victory.
    /// </summary>
    public IEnumerator ResolveCombat(PlayerState player, PatrolEnemy enemy)
    {
        if (!IsServer) yield break;

        int playerDice = player.Ship.AttackDice;  // Base ship attack
        int enemyDice  = enemy.AttackDice;

        var playerRoll = DiceRoller.Roll(playerDice, standardDie);
        var enemyRoll  = DiceRoller.Roll(enemyDice,  standardDie);

        bool playerWon = playerRoll.Hits > enemyRoll.Hits;
        int  margin    = Mathf.Abs(playerRoll.Hits - enemyRoll.Hits);

        var outcome = new CombatOutcome
        {
            PlayerWon     = playerWon,
            DamageToEnemy  = playerWon ? margin + playerRoll.Crits : 0,
            DamageToPlayer = playerWon ? 0 : margin + enemyRoll.Crits,
            PlayerRoll    = playerRoll,
            EnemyRoll     = enemyRoll
        };

        // Apply damage.
        if (outcome.DamageToPlayer > 0)
            player.TakeHullDamage(outcome.DamageToPlayer);

        if (outcome.DamageToEnemy >= enemy.HullPoints)
        {
            // Enemy destroyed: apply rewards.
            player.AddCredits(enemy.RewardCredits);
            player.AddFame(enemy.RewardFame);
            player.ModifyReputation(enemy.Faction, enemy.ReputationReward);
        }

        // Broadcast roll results to all clients for animation.
        BroadcastCombatResultClientRpc(
            player.OwnerClientId,
            SerializeDieFaces(playerRoll),
            SerializeDieFaces(enemyRoll),
            outcome.PlayerWon,
            outcome.DamageToPlayer,
            outcome.DamageToEnemy);

        yield return new WaitForSeconds(2.5f); // Wait for dice animation
        GameManager.Instance?.NotifyEncounterComplete();
    }

    [ClientRpc]
    private void BroadcastCombatResultClientRpc(
        ulong  playerClientId,
        int[]  playerFaces,
        int[]  enemyFaces,
        bool   playerWon,
        int    damageToPlayer,
        int    damageToEnemy)
    {
        // Trigger dice roll animation on all clients' UI.
        UIManager.Instance?.ShowCombatResult(
            playerClientId, playerFaces, enemyFaces,
            playerWon, damageToPlayer, damageToEnemy);
    }

    // ─── Skill Check ──────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a non-combat skill check (e.g., Encounter card).
    /// Returns true if player's hits meet or exceed the difficulty threshold.
    /// </summary>
    public bool ResolveSkillCheck(PlayerState player, SkillType skill, int difficulty, out DiceRollResult rollResult)
    {
        int numDice = player.GetSkillValue(skill);
        rollResult  = DiceRoller.Roll(numDice, standardDie);
        return rollResult.Hits >= difficulty;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private int[] SerializeDieFaces(DiceRollResult roll) =>
        roll.Faces.ConvertAll(f => (int)f).ToArray();
}

// ─────────────────────────────────────────────────────────────────────────────

// PatrolEnemy.cs — Plain data class. Not a MonoBehaviour.
[System.Serializable]
public class PatrolEnemy
{
    public string      Name;
    public FactionType Faction;
    public int         AttackDice;
    public int         HullPoints;
    public int         RewardCredits;
    public int         RewardFame;
    public int         ReputationReward; // e.g. +1 Reputation with Faction on defeat
}
```

### 6.3 Encounter Resolver

```csharp
// EncounterResolver.cs
// Determines what encounter type to run for the active player's current node
// and orchestrates the resolution flow. Called by GameManager after Action Phase.

using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class EncounterResolver : NetworkBehaviour
{
    public static EncounterResolver Instance { get; private set; }

    [Header("Patrol Definitions")]
    [SerializeField] private PatrolEnemyDatabase patrolDatabase;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// Entry point called by GameManager at the start of the Encounter Phase.
    /// Determines which of the three encounter types applies and routes accordingly.
    /// </summary>
    public void ResolveEncounter(PlayerState player)
    {
        if (!IsServer) return;
        StartCoroutine(EncounterCoroutine(player));
    }

    private IEnumerator EncounterCoroutine(PlayerState player)
    {
        var node = MapManager.Instance?.GetNodeById(player.CurrentNodeId);
        if (node == null)
        {
            GameManager.Instance?.NotifyEncounterComplete();
            yield break;
        }

        // ── Priority 1: Faction Patrol (negative reputation) ─────────────────
        if (ShouldTriggerPatrol(player, node))
        {
            var patrol = patrolDatabase?.GetPatrolForFaction(node.FactionOwner);
            if (patrol != null)
            {
                yield return CombatResolver.Instance?.ResolveCombat(player, patrol);
                yield break; // Combat coroutine calls NotifyEncounterComplete
            }
        }

        // ── Priority 2: Contact Token ────────────────────────────────────────
        if (node.HasContactToken && node.ContactToken != null)
        {
            yield return ResolveContactToken(player, node);
            yield break;
        }

        // ── Priority 3: Planet Card ──────────────────────────────────────────
        if (node.NodeType == MapNodeType.Planet)
        {
            var card = DeckManager.Instance?.DrawPlanetEncounterCard(node.PlanetId);
            if (card != null)
            {
                yield return ResolvePlanetCard(player, card);
                yield break;
            }
        }

        // NavPoint with no encounters: phase ends immediately.
        GameManager.Instance?.NotifyEncounterComplete();
    }

    private bool ShouldTriggerPatrol(PlayerState player, MapNode node)
    {
        if (node.FactionOwner == FactionType.None) return false;
        return player.GetReputationStatus(node.FactionOwner) == ReputationStatus.Negative;
    }

    private IEnumerator ResolvePlanetCard(PlayerState player, EncounterCardData card)
    {
        ShowEncounterCardClientRpc(player.OwnerClientId, card.CardId);
        yield return new WaitForSeconds(1.5f); // Show card to player

        if (card.RequiresCheck)
        {
            bool success = CombatResolver.Instance.ResolveSkillCheck(
                player, card.CheckSkill, card.CheckDifficulty, out var roll);

            BroadcastSkillCheckResultClientRpc(
                player.OwnerClientId, (int)card.CheckSkill,
                SerializeFaces(roll), success);

            yield return new WaitForSeconds(2f);

            if (success)
            {
                player.AddCredits(card.SuccessCredits);
                player.AddFame(card.SuccessFame);
                player.ModifyReputation(card.SuccessRepFaction, card.SuccessRepDelta);
            }
            else
            {
                player.TakeScoundrelDamage(card.FailureDamage);
                player.SpendCredits(card.FailureCreditsLost);
                player.ModifyReputation(card.FailureRepFaction, card.FailureRepDelta);
            }
        }

        GameManager.Instance?.NotifyEncounterComplete();
    }

    private IEnumerator ResolveContactToken(PlayerState player, MapNode node)
    {
        // Reveal the face-down token.
        RevealContactTokenClientRpc(player.OwnerClientId, node.NodeId);
        yield return new WaitForSeconds(1f);

        var token = node.ContactToken;
        // Apply token effects (varies per token type — extend as needed).
        if (token != null) token.Apply(player);

        // Remove token from board.
        node.HasContactToken = false;
        RemoveContactTokenClientRpc(node.NodeId);

        GameManager.Instance?.NotifyEncounterComplete();
    }

    [ClientRpc] private void ShowEncounterCardClientRpc(ulong clientId, int cardId)        { UIManager.Instance?.ShowEncounterCard(clientId, cardId); }
    [ClientRpc] private void BroadcastSkillCheckResultClientRpc(ulong c, int skill, int[] faces, bool success) { UIManager.Instance?.ShowSkillCheckResult(c, skill, faces, success); }
    [ClientRpc] private void RevealContactTokenClientRpc(ulong clientId, int nodeId)       { MapManager.Instance?.GetNodeById(nodeId)?.GetComponent<MapNodeVisuals>()?.RevealToken(); }
    [ClientRpc] private void RemoveContactTokenClientRpc(int nodeId)                       { MapManager.Instance?.GetNodeById(nodeId)?.GetComponent<MapNodeVisuals>()?.RemoveToken(); }

    private int[] SerializeFaces(DiceRollResult roll) =>
        roll.Faces.ConvertAll(f => (int)f).ToArray();
}
```

---

## 7. ScriptableObject Definitions

```csharp
// ScoundrelData.cs
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewScoundrel", menuName = "OuterRim/Characters/Scoundrel")]
public class ScoundrelData : ScriptableObject
{
    [Header("Identity")]
    public string CharacterName;
    public Sprite CharacterPortrait;
    [TextArea(2, 4)]
    public string PersonalGoal;        // Displayed in goal tracker UI
    public int    PersonalGoalFame;    // Fame earned for completing personal goal

    [Header("Starting Stats")]
    public int MaxHealth       = 3;
    public int StartingCredits = 1000;
    public int StartingNodeId  = 0;    // Which MapNode they begin on

    [Header("Skills")]
    [Range(1, 5)] public int CombatSkill   = 2;
    [Range(1, 5)] public int PilotingSkill = 2;
    [Range(1, 5)] public int CunningSkill  = 2;
    [Range(1, 5)] public int TechSkill     = 2;

    [Header("Starting Equipment")]
    public ShipData           StartingShip;
    public List<GearCardData> StartingGear;
}

// ─────────────────────────────────────────────────────────────────────────────

// ShipData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "NewShip", menuName = "OuterRim/Ships/Ship")]
public class ShipData : ScriptableObject
{
    [Header("Identity")]
    public string ShipName;
    public Sprite ShipArt;
    public GameObject ShipPrefab;   // 3D model prefab

    [Header("Stats")]
    [Range(1, 6)] public int Speed        = 2;
    [Range(1, 8)] public int MaxHullHealth = 4;
    [Range(1, 4)] public int AttackDice   = 1;

    [Header("Slots")]
    [Range(0, 6)] public int CargoSlots = 2;
    [Range(0, 4)] public int CrewSlots  = 1;
    [Range(0, 4)] public int ModSlots   = 1;
}

// ─────────────────────────────────────────────────────────────────────────────

// CharacterRegistry.cs
// Singleton SO-registry so all clients can look up ScoundrelData/ShipData by index.
// This enables syncing character selection as a simple int (index) over the network.

using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterRegistry", menuName = "OuterRim/Character Registry")]
public class CharacterRegistry : ScriptableObject
{
    private static CharacterRegistry _instance;
    public static CharacterRegistry Instance
    {
        get
        {
            if (_instance == null)
                _instance = Resources.Load<CharacterRegistry>("CharacterRegistry");
            return _instance;
        }
    }

    public List<ScoundrelData> Scoundrels;
    public List<ShipData>      Ships;

    public ScoundrelData GetScoundrel(int index) =>
        index >= 0 && index < Scoundrels.Count ? Scoundrels[index] : null;

    public ShipData GetShip(int index) =>
        index >= 0 && index < Ships.Count ? Ships[index] : null;
}
```

---

## 8. Implementation Roadmap

Suggested sprint order for a solo or small team:

### Phase 1 — Foundation (Weeks 1–3)
- [ ] Unity project setup: install NGO, Unity Relay, Unity Lobby packages  
- [ ] `NetworkBootstrapper`: implement host/join flow with Relay JoinCode  
- [ ] Stub `GameManager` with phase enum and basic `TransitionToPhase` loop  
- [ ] `PlayerState` with Fame, Credits, Health NetworkVariables  
- [ ] `MapNode` with hardcoded 10-node test map  
- [ ] `MapManager`: BFS `GetReachableNodes` + `FindPath` with unit tests  

### Phase 2 — Core Gameplay (Weeks 4–7)
- [ ] Planning Phase RPCs: MoveShip / Heal / CollectCredits  
- [ ] Ship movement animation along node path  
- [ ] `DeckManager` + `MarketDeck` with SO-loaded card data  
- [ ] Action Phase: `BuyCard`, `CycleCard` ServerRpcs  
- [ ] Win condition check loop  
- [ ] `DiceFaceDistribution` + `DiceRoller`  
- [ ] `CombatResolver.ResolveCombat` with ClientRpc result broadcast  

### Phase 3 — Encounter System (Weeks 8–10)
- [ ] `EncounterResolver`: patrol detection via reputation  
- [ ] Planet encounter card draw and skill check flow  
- [ ] Contact token reveal mechanic  
- [ ] Faction reputation system (modify + display)  
- [ ] Action Phase: `DeliverCargo`, `CompleteBounty`, `TradeWithPlayer` ServerRpcs  

### Phase 4 — Content & Polish (Weeks 11–14)
- [ ] Full map layout (20+ nodes, all hyperspace lanes)  
- [ ] All 6 card decks populated (Bounties, Cargo, Gear, Mods, Jobs, Luxury)  
- [ ] All Scoundrel + Ship ScriptableObjects  
- [ ] Character selection lobby screen  
- [ ] UI: turn phase indicators, fame tracker, market rows, reputation HUD  
- [ ] 3D ship models, node VFX (hyperspace lane highlights)  

### Phase 5 — QA & Launch Prep (Weeks 15–16)
- [ ] Playtest 2-player and 4-player sessions  
- [ ] Edge case testing: host disconnect, mid-game reconnect  
- [ ] Balance pass on card costs, patrol strengths, fame rewards  
- [ ] Performance profiling (NetworkList updates, BFS on large maps)  

---

## Key Design Decisions Summary

| Decision | Choice | Rationale |
|---|---|---|
| Networking library | Unity NGO + Relay | First-party, no dedicated server cost, NAT-safe |
| Authority model | Host-Server (one player is host) | Sufficient for turn-based; simpler than dedicated server |
| State sync method | NetworkVariable + NetworkList | Server-authoritative; clients react via event subscriptions |
| Deck contents visibility | Server-only `List<T>` | Prevents cheating; only market row revealed via ClientRpc |
| Card data pipeline | ScriptableObjects (JSON as fallback) | Designer-friendly, Inspector-editable, no code changes for new cards |
| Pathfinding algorithm | BFS (unweighted) | All edges cost 1 move; simpler and sufficient for board game scale |
| Node IDs | `int` (Inspector-assigned) | Simple, fast dict lookup; no need for GUID overhead |
| Dice | Server-side roll, broadcast faces | Prevents client-side manipulation; all clients see same result |

---

*End of Technical Design Document v1.0*
