// PlayerState.cs — V2 per Outer Rim rulebooks
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class PlayerState : NetworkBehaviour
    {
        // ─── Core Stats ──────────────────────────────────────────────────────
        public NetworkVariable<int> Fame = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Credits = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Health = new(5, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> MaxHealth = new(5);
        public NetworkVariable<int> ShipHealth = new(3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> MaxShipHealth = new(3);
        public NetworkVariable<int> Hyperdrive = new(2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> AttackDice = new(2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ─── Ship Slots ──────────────────────────────────────────────────────
        public NetworkVariable<int> CargoSlots = new(2);
        public NetworkVariable<int> CargoUsed = new(0);
        public NetworkVariable<int> BountiesHeld = new(0);           // Bounties occupy cargo space but tracked separately for delivery
        public NetworkVariable<int> CrewSlots = new(1);
        public NetworkVariable<int> CrewUsed = new(0);
        public NetworkVariable<int> GearSlots = new(1);
        public NetworkVariable<int> GearUsed = new(0);
        public NetworkVariable<int> ModSlots = new(1);
        public NetworkVariable<int> ModUsed = new(0);

        // ─── V2 Reputation: 3 discrete states per faction ────────────────────
        public NetworkVariable<ReputationStatus> HuttRep = new(ReputationStatus.Neutral, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ReputationStatus> SyndicateRep = new(ReputationStatus.Neutral, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ReputationStatus> ImperialRep = new(ReputationStatus.Neutral, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<ReputationStatus> RebelRep = new(ReputationStatus.Neutral, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ─── Map ─────────────────────────────────────────────────────────────
        public NetworkVariable<int> CurrentNodeId = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ─── V2 Skills (7 per rulebook) ──────────────────────────────────────
        public NetworkVariable<int> InfluenceSkill = new(1);
        public NetworkVariable<int> StrengthSkill = new(1);
        public NetworkVariable<int> KnowledgeSkill = new(1);
        public NetworkVariable<int> TacticsSkill = new(1);
        public NetworkVariable<int> PilotingSkill = new(1);
        public NetworkVariable<int> StealthSkill = new(1);
        public NetworkVariable<int> TechSkill = new(1);

        // ─── Combat Values ───────────────────────────────────────────────────
        public NetworkVariable<int> GroundCombatValue = new(2);
        public NetworkVariable<int> ShipCombatValue = new(2);

        // ─── Defeated State ──────────────────────────────────────────────────
        public NetworkVariable<bool> IsDefeated = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ─── Identity ────────────────────────────────────────────────────────
        public NetworkVariable<Unity.Collections.FixedString32Bytes> PlayerName = new(default);
        public NetworkVariable<int> CharacterId = new(-1);

        // ─── Client ──────────────────────────────────────────────────────────
        public bool IsMyTurn { get; private set; }

        // ─── Helpers ─────────────────────────────────────────────────────────
        public ReputationStatus GetReputation(FactionType f) => f switch
        {
            FactionType.Hutt      => HuttRep.Value,
            FactionType.Syndicate => SyndicateRep.Value,
            FactionType.Imperial  => ImperialRep.Value,
            FactionType.Rebel     => RebelRep.Value,
            _ => ReputationStatus.Neutral
        };

        public void SetReputation(FactionType f, ReputationStatus status)
        {
            if (!IsServer) return;
            switch (f)
            {
                case FactionType.Hutt:      HuttRep.Value = status; break;
                case FactionType.Syndicate: SyndicateRep.Value = status; break;
                case FactionType.Imperial:  ImperialRep.Value = status; break;
                case FactionType.Rebel:     RebelRep.Value = status; break;
            }
        }

        public int GetSkillValue(SkillType s) => s switch
        {
            SkillType.Influence => InfluenceSkill.Value,
            SkillType.Strength  => StrengthSkill.Value,
            SkillType.Knowledge => KnowledgeSkill.Value,
            SkillType.Tactics   => TacticsSkill.Value,
            SkillType.Piloting  => PilotingSkill.Value,
            SkillType.Stealth   => StealthSkill.Value,
            SkillType.Tech      => TechSkill.Value,
            _ => 1
        };

        public int GetEffectiveHyperdrive() => Hyperdrive.Value; // + mod bonuses later

        // ─── Resources ───────────────────────────────────────────────────────
        public bool SpendCredits(int amount)
        {
            if (!IsServer || Credits.Value < amount || amount < 0) return false;
            Credits.Value -= amount;
            return true;
        }
        public void AddCredits(int amount) { if (IsServer && amount > 0) Credits.Value += amount; }
        public void AddFame(int amount) { if (IsServer && amount > 0) Fame.Value += amount; }
        public void SetStartingCredits(int amount) { if (IsServer) Credits.Value = amount; }

        // ─── Defeated ────────────────────────────────────────────────────────
        public void ApplyDefeat()
        {
            if (!IsServer) return;
            Credits.Value = Mathf.Max(0, Credits.Value - 3000); // V2: flat 3000 penalty
            Health.Value = MaxHealth.Value;
            ShipHealth.Value = MaxShipHealth.Value;
            IsDefeated.Value = true;
        }

        public void RecoverAllDamage()
        {
            if (!IsServer) return;
            Health.Value = MaxHealth.Value;
            ShipHealth.Value = MaxShipHealth.Value;
        }

        public void StandUp() { if (IsServer) IsDefeated.Value = false; }

        public void MoveToNode(int nodeId) { if (IsServer) CurrentNodeId.Value = nodeId; }

        // ─── Damage ──────────────────────────────────────────────────────────
        public void TakeHullDamage(int amount)
        {
            if (!IsServer || amount <= 0) return;
            ShipHealth.Value = Mathf.Max(0, ShipHealth.Value - amount);
            if (ShipHealth.Value <= 0)
            {
                Health.Value = Mathf.Max(0, Health.Value - 1);
                ShipHealth.Value = MaxShipHealth.Value;
                if (Health.Value <= 0) ApplyDefeat();
            }
        }

        public void TakeGroundDamage(int amount)
        {
            if (!IsServer || amount <= 0) return;
            Health.Value = Mathf.Max(0, Health.Value - amount);
            if (Health.Value <= 0) ApplyDefeat();
        }

        public void SetTurnActive(bool active) => IsMyTurn = active;

        public override void OnNetworkSpawn()
        {
            if (IsOwner) SetPlayerNameServerRpc($"Player {OwnerClientId}");
        }

        [ServerRpc] private void SetPlayerNameServerRpc(string n) => PlayerName.Value = n;

        // ─── Effective Values (with gear/mod bonuses) ────────────────────────
        public int GetEffectiveSkill(SkillType skill) => skill switch
        {
            SkillType.Influence => InfluenceSkill.Value,
            SkillType.Strength   => StrengthSkill.Value,
            SkillType.Knowledge  => KnowledgeSkill.Value,
            SkillType.Tactics    => TacticsSkill.Value,
            SkillType.Piloting   => PilotingSkill.Value,
            SkillType.Stealth    => StealthSkill.Value,
            SkillType.Tech       => TechSkill.Value,
            _ => 1
        };
        // TODO: add gear/mod bonus once equipment inventory is tracked

        public int GetEffectiveSpeed() => Hyperdrive.Value;
        // TODO: add speed modifier from ship mods
    }
}
