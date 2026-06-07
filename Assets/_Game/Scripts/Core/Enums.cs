// Enums.cs — Shared enumerations for Outer Rim Digital
namespace OuterRim
{
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
        MoveShip,
        HealDamage,
        CollectCredits
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
        Syndicate,
        Authority,
        Rebels,
        Hutts,
        None
    }

    public enum ReputationStatus
    {
        Negative = -1,
        Neutral = 0,
        Positive = 1
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

    // Serializable struct for card instances stored in NetworkLists.
    [System.Serializable]
    public struct CardInstanceData : Unity.Netcode.INetworkSerializable, System.IEquatable<CardInstanceData>
    {
        public int CardDefinitionId;
        public MarketDeckType DeckType;

        public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> serializer) where T : Unity.Netcode.IReaderWriter
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
}
