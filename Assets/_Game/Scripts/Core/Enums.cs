// Enums.cs — V2 (Rules-accurate per Outer Rim rulebooks)
// Factions, skills, reputation, dice, market decks all corrected from V1.
using Unity.Netcode;

namespace OuterRim
{
    // ─── Game Phase ──────────────────────────────────────────────────────────
    public enum GamePhase
    {
        WaitingForPlayers,
        PlanningPhase,
        ActionPhase,
        EncounterPhase,
        ResolvingCombat,
        ResolvingEncounterCard,
        ResolvingContact,
        ResolvingJob,
        CheckingWinCondition,
        GameOver
    }

    // ─── Planning ────────────────────────────────────────────────────────────
    public enum PlanningChoice
    {
        None,
        MoveShip,
        RecoverDamage,   // Remove ALL damage from character AND ship
        GainCredits      // Gain 2,000 credits
    }

    // ─── V2 Market Decks (6 total — Gear/Mod combined, Ship added) ──────────
    public enum MarketDeckType
    {
        Bounty,       // 11 base cards
        Cargo,        // 10 base cards
        GearAndMod,   // Gear + Mod combined. 15 base cards.
        Job,          // 14 base cards
        Luxury,       // 11 base cards
        Ship          // 9 base cards
    }

    // ─── V2 Factions (correct names from rulebook) ───────────────────────────
    public enum FactionType
    {
        Hutt,
        Syndicate,
        Imperial,
        Rebel,
        None
    }

    // ─── V2 Reputation (3 discrete states, NOT a number range) ───────────────
    public enum ReputationStatus
    {
        Positive,
        Neutral,
        Negative
    }

    // ─── Map ─────────────────────────────────────────────────────────────────
    public enum MapNodeType
    {
        Planet,
        NavPoint,
        Maelstrom   // Special — ends movement, extra-turn encounter
    }

    // ─── V2 Skills (7 skills per rulebook character/crew cards) ──────────────
    public enum SkillType
    {
        Influence,
        Strength,
        Knowledge,
        Tactics,
        Piloting,
        Stealth,
        Tech
    }

    // ─── V2 Dice (8-sided: distribution per rulebook) ────────────────────────
    public enum DieFace
    {
        Blank,   // 2 sides
        Focus,   // 2 sides
        Hit,     // 3 sides — 1 damage
        Crit     // 1 side — 2 damage
    }

    // ─── Combat ──────────────────────────────────────────────────────────────
    public enum CombatType
    {
        Ground,  // Character combat value + health
        Ship     // Ship combat value + hull
    }

    public enum PatrolLevel
    {
        Level1 = 1,  // Credit reward
        Level2 = 2,  // Fame reward
        Level3 = 3,  // Higher fame reward
        Level4 = 4   // INVULNERABLE — always defeats player
    }

    // ─── Contact Tokens ──────────────────────────────────────────────────────
    public enum ContactClass
    {
        White,   // Least dangerous
        Green,
        Yellow,
        Orange   // Most dangerous (Unfinished Business)
    }

    // ─── Encounter Choice ────────────────────────────────────────────────────
    public enum EncounterChoice
    {
        EncounterPatrol,
        EncounterSpace,
        EncounterContact,
        ResolveCardAbility
    }

    // ─── Network-serializable card instance ──────────────────────────────────
    [System.Serializable]
    public struct CardInstanceData : INetworkSerializable, System.IEquatable<CardInstanceData>
    {
        public int CardDefinitionId;
        public MarketDeckType DeckType;
        public bool IsRotated; // Unfinished Business: rotating assets

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref CardDefinitionId);
            int dt = (int)DeckType;
            s.SerializeValue(ref dt);
            DeckType = (MarketDeckType)dt;
            s.SerializeValue(ref IsRotated);
        }

        public bool Equals(CardInstanceData other) =>
            CardDefinitionId == other.CardDefinitionId && DeckType == other.DeckType;
        public override int GetHashCode() => System.HashCode.Combine(CardDefinitionId, (int)DeckType);
    }
}
