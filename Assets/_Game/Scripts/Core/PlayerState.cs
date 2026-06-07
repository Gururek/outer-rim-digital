// PlayerState.cs — Per-player networked state
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class PlayerState : NetworkBehaviour
    {
        // ─── Networked State (server-authoritative) ──────────────────────────
        public NetworkVariable<int> Fame = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> Credits = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> Health = new NetworkVariable<int>(
            5, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> MaxHealth = new NetworkVariable<int>(
            5, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> ShipHealth = new NetworkVariable<int>(
            3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> MaxShipHealth = new NetworkVariable<int>(
            3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> Speed = new NetworkVariable<int>(
            2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Reputation per faction: Syndicate, Authority, Rebels, Hutts
        public NetworkVariable<int> SyndicateRep = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> AuthorityRep = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> RebelsRep = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> HuttsRep = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Current map node ID
        public NetworkVariable<int> CurrentNodeId = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Inventory: cargo, crew, gear, mods — synced for all to see
        public NetworkList<CardInstanceData> Inventory;

        // Player name (set once on spawn, doesn't change)
        public NetworkVariable<Unity.Collections.FixedString32Bytes> PlayerName = new NetworkVariable<Unity.Collections.FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Character ID reference
        public NetworkVariable<int> CharacterId = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ─── Client-side state ──────────────────────────────────────────────
        public bool IsMyTurn { get; private set; }

        // ─── Public helpers ─────────────────────────────────────────────────

        public int GetReputation(FactionType faction)
        {
            return faction switch
            {
                FactionType.Syndicate => SyndicateRep.Value,
                FactionType.Authority => AuthorityRep.Value,
                FactionType.Rebels => RebelsRep.Value,
                FactionType.Hutts => HuttsRep.Value,
                _ => 0
            };
        }

        public void ModifyReputation(FactionType faction, int delta)
        {
            if (!IsServer) return;
            switch (faction)
            {
                case FactionType.Syndicate: SyndicateRep.Value += delta; break;
                case FactionType.Authority: AuthorityRep.Value += delta; break;
                case FactionType.Rebels: RebelsRep.Value += delta; break;
                case FactionType.Hutts: HuttsRep.Value += delta; break;
            }
        }

        public ReputationStatus GetReputationStatus(FactionType faction)
        {
            int rep = GetReputation(faction);
            if (rep > 0) return ReputationStatus.Positive;
            if (rep < 0) return ReputationStatus.Negative;
            return ReputationStatus.Neutral;
        }

        public void SetTurnActive(bool active)
        {
            IsMyTurn = active;
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                // Request name from lobby or local settings
                if (Unity.Services.Lobby.LobbyService.Instance.CurrentLobby != null)
                {
                    var localPlayer = Unity.Services.Lobby.LobbyService.Instance.CurrentLobby.Players
                        .Find(p => p.Id == Unity.Services.Authentication.AuthenticationService.Instance.PlayerId);
                    if (localPlayer != null)
                    {
                        SetPlayerNameServerRpc(localPlayer.Data?.ContainsKey("name") == true
                            ? localPlayer.Data["name"].Value
                            : $"Player {OwnerClientId}");
                    }
                }
                else
                {
                    SetPlayerNameServerRpc($"Player {OwnerClientId}");
                }
            }
        }

        [ServerRpc]
        private void SetPlayerNameServerRpc(string name)
        {
            PlayerName.Value = name;
        }
    }
}
