// PlayerState.cs — Per-player networked state (Outer Rim rules)
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class PlayerState : NetworkBehaviour
    {
        // ─── Core Stats ──────────────────────────────────────────────────
        public NetworkVariable<int> Fame = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Credits = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Health = new(5, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> MaxHealth = new(5);
        public NetworkVariable<int> ShipHealth = new(3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> MaxShipHealth = new(3);
        public NetworkVariable<int> Speed = new(2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> AttackDice = new(2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ─── Ship Slots (per Outer Rim rules) ────────────────────────────
        public NetworkVariable<int> CargoSlots = new(2);
        public NetworkVariable<int> CargoUsed = new(0);
        public NetworkVariable<int> CrewSlots = new(1);
        public NetworkVariable<int> CrewUsed = new(0);
        public NetworkVariable<int> GearSlots = new(1);
        public NetworkVariable<int> GearUsed = new(0);
        public NetworkVariable<int> ModSlots = new(1);
        public NetworkVariable<int> ModUsed = new(0);

        // ─── Reputation (per faction) ────────────────────────────────────
        public NetworkVariable<int> SyndicateRep = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> AuthorityRep = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> RebelsRep = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> HuttsRep = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ─── Map Location ────────────────────────────────────────────────
        public NetworkVariable<int> CurrentNodeId = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ─── Skills ──────────────────────────────────────────────────────
        public NetworkVariable<int> CombatSkill = new(2);
        public NetworkVariable<int> PilotingSkill = new(1);
        public NetworkVariable<int> CunningSkill = new(1);
        public NetworkVariable<int> TechSkill = new(1);

        // ─── Inventory ───────────────────────────────────────────────────
        public NetworkList<CardInstanceData> Inventory = new();

        // ─── Defeated State (per Outer Rim rules) ────────────────────────
        public NetworkVariable<bool> IsDefeated = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ─── Identity ────────────────────────────────────────────────────
        public NetworkVariable<Unity.Collections.FixedString32Bytes> PlayerName = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> CharacterId = new(-1);

        // ─── Client-side ─────────────────────────────────────────────────
        public bool IsMyTurn { get; private set; }
        public bool HasSlotFor(MarketDeckType cardType)
        {
            return cardType switch
            {
                MarketDeckType.Cargo  => CargoUsed.Value < CargoSlots.Value,
                MarketDeckType.Gear   => GearUsed.Value < GearSlots.Value,
                MarketDeckType.Mods   => ModUsed.Value < ModSlots.Value,
                MarketDeckType.Jobs   => true, // jobs don't use ship slots
                MarketDeckType.Bounties => true,
                MarketDeckType.Luxury  => true,
                _ => false
            };
        }

        // ─── Reputation Helpers ──────────────────────────────────────────
        public int GetReputation(FactionType faction) => faction switch
        {
            FactionType.Syndicate => SyndicateRep.Value,
            FactionType.Authority => AuthorityRep.Value,
            FactionType.Rebels    => RebelsRep.Value,
            FactionType.Hutts     => HuttsRep.Value,
            _ => 0
        };

        public void ModifyReputation(FactionType faction, int delta)
        {
            if (!IsServer) return;
            switch (faction)
            {
                case FactionType.Syndicate: SyndicateRep.Value = Mathf.Clamp(SyndicateRep.Value + delta, -4, 4); break;
                case FactionType.Authority: AuthorityRep.Value = Mathf.Clamp(AuthorityRep.Value + delta, -4, 4); break;
                case FactionType.Rebels:    RebelsRep.Value = Mathf.Clamp(RebelsRep.Value + delta, -4, 4); break;
                case FactionType.Hutts:     HuttsRep.Value = Mathf.Clamp(HuttsRep.Value + delta, -4, 4); break;
            }
        }

        public ReputationStatus GetReputationStatus(FactionType faction)
        {
            int rep = GetReputation(faction);
            if (rep > 0) return ReputationStatus.Positive;
            if (rep < 0) return ReputationStatus.Negative;
            return ReputationStatus.Neutral;
        }

        // ─── Resources ───────────────────────────────────────────────────
        public bool SpendCredits(int amount)
        {
            if (!IsServer || Credits.Value < amount || amount < 0) return false;
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

        // ─── Defeated State (Outer Rim rules) ────────────────────────────
        /// <summary>
        /// Apply defeated penalties: lose half credits, discard a crew member,
        /// lose all damage, respawn at nearest planet or starting location.
        /// After being defeated, the player must choose "Heal" during their next Planning Step.
        /// </summary>
        public void ApplyDefeat()
        {
            if (!IsServer) return;
            Credits.Value = Credits.Value / 2;
            // Lose a crew member if any (handled by inventory system later)
            Health.Value = MaxHealth.Value;
            ShipHealth.Value = MaxShipHealth.Value;
            IsDefeated.Value = true;
            Debug.Log($"[PlayerState] Player {OwnerClientId} DEFEATED! Credits halved to {Credits.Value}, respawning.");
        }

        public void RecoverFromDefeat()
        {
            if (!IsServer) return;
            IsDefeated.Value = false;
        }

        // ─── Damage ──────────────────────────────────────────────────────
        public void TakeHullDamage(int amount)
        {
            if (!IsServer || amount <= 0) return;
            ShipHealth.Value = Mathf.Max(0, ShipHealth.Value - amount);
            if (ShipHealth.Value <= 0)
            {
                // Ship destroyed — character takes damage (Outer Rim: ship destroyed = 1 damage to character)
                Health.Value = Mathf.Max(0, Health.Value - 1);
                ShipHealth.Value = MaxShipHealth.Value;
                Debug.Log($"[PlayerState] Player {OwnerClientId} ship destroyed! Health: {Health.Value}/{MaxHealth.Value}");

                if (Health.Value <= 0)
                    ApplyDefeat();
            }
        }

        // ─── Skills ──────────────────────────────────────────────────────
        public int GetSkillValue(SkillType skill) => skill switch
        {
            SkillType.Combat   => CombatSkill.Value,
            SkillType.Piloting => PilotingSkill.Value,
            SkillType.Cunning  => CunningSkill.Value,
            SkillType.Tech     => TechSkill.Value,
            _ => 1
        };

        public void SetTurnActive(bool active) => IsMyTurn = active;

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
                SetPlayerNameServerRpc($"Player {OwnerClientId}");
        }

        [ServerRpc]
        private void SetPlayerNameServerRpc(string name) => PlayerName.Value = name;
    }
}
