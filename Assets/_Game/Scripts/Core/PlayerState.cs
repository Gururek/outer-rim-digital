// PlayerState.cs — Per-player networked state
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class PlayerState : NetworkBehaviour
    {
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
        public NetworkVariable<int> SyndicateRep = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> AuthorityRep = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> RebelsRep = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> HuttsRep = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> CurrentNodeId = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> AttackDice = new NetworkVariable<int>(
            2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> CombatSkill = new NetworkVariable<int>(
            2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> PilotingSkill = new NetworkVariable<int>(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> CunningSkill = new NetworkVariable<int>(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> TechSkill = new NetworkVariable<int>(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkList<CardInstanceData> Inventory = new NetworkList<CardInstanceData>();

        public NetworkVariable<Unity.Collections.FixedString32Bytes> PlayerName = new NetworkVariable<Unity.Collections.FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> CharacterId = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool IsMyTurn { get; private set; }

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

        public bool SpendCredits(int amount)
        {
            if (!IsServer) return false;
            if (Credits.Value < amount || amount < 0) return false;
            Credits.Value -= amount;
            return true;
        }

        public void AddCredits(int amount)
        {
            if (!IsServer || amount <= 0) return;
            Credits.Value += amount;
        }

        public void AddFame(int amount)
        {
            if (!IsServer || amount <= 0) return;
            Fame.Value += amount;
        }

        public void TakeHullDamage(int amount)
        {
            if (!IsServer || amount <= 0) return;
            ShipHealth.Value = Mathf.Max(0, ShipHealth.Value - amount);
            if (ShipHealth.Value <= 0 && Health.Value > 0)
            {
                Health.Value = Mathf.Max(0, Health.Value - 1);
                ShipHealth.Value = MaxShipHealth.Value;
                Debug.Log($"[PlayerState] Player {OwnerClientId} ship destroyed! Scoundrel health: {Health.Value}");
            }
        }

        public int GetSkillValue(SkillType skill)
        {
            return skill switch
            {
                SkillType.Combat   => CombatSkill.Value,
                SkillType.Piloting => PilotingSkill.Value,
                SkillType.Cunning  => CunningSkill.Value,
                SkillType.Tech     => TechSkill.Value,
                _ => 1
            };
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
                SetPlayerNameServerRpc($"Player {OwnerClientId}");
            }
        }

        [ServerRpc]
        private void SetPlayerNameServerRpc(string name)
        {
            PlayerName.Value = name;
        }
    }
}
