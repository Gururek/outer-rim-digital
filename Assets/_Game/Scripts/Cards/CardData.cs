// CardData.cs — V2 ScriptableObject hierarchy (rules-accurate market decks)
using UnityEngine;

namespace OuterRim
{
    [CreateAssetMenu(fileName = "NewCard", menuName = "Outer Rim/Cards/Base Card")]
    public class CardData : ScriptableObject
    {
        [Header("Identity")]
        public int CardId;
        public string CardName;
        [TextArea(2,4)] public string FlavorText;
        public Sprite CardArt;
        public MarketDeckType DeckType;

        [Header("Market")]
        public int BuyCost;
        public int SellValue;

        [Header("Effect")]
        [TextArea(2,5)] public string EffectDescription;
    }

    // ─── Bounty ─────────────────────────────────────────────────────────────
    [CreateAssetMenu(fileName = "NewBounty", menuName = "Outer Rim/Cards/Bounty")]
    public class BountyCardData : CardData
    {
        public string TargetName;
        public FactionType IssuingFaction;
        public int RewardCredits;
        public int RewardFame;
        public ContactClass ContactClass;
        public int CombatValue;
    }

    // ─── Cargo ──────────────────────────────────────────────────────────────
    [CreateAssetMenu(fileName = "NewCargo", menuName = "Outer Rim/Cards/Cargo")]
    public class CargoCardData : CardData
    {
        public string DeliveryPlanetId;
        public int DeliveryReward;
        public bool IsIllegal;
        public int IllegalFameReward;
    }

    // ─── GearAndMod (V2: combined deck) ─────────────────────────────────────
    [CreateAssetMenu(fileName = "NewGear", menuName = "Outer Rim/Cards/Gear")]
    public class GearCardData : CardData
    {
        public SkillType AffectedSkill;
        public int SkillBonus;
        public bool IsSingleUse;
    }

    [CreateAssetMenu(fileName = "NewMod", menuName = "Outer Rim/Cards/Mod")]
    public class ModCardData : CardData
    {
        public int SpeedBonus;
        public int HullBonus;
        public int ExtraCargoSlots;
        public int ExtraCrewSlots;
        public int AttackDiceBonus;
    }

    // ─── Job ────────────────────────────────────────────────────────────────
    [CreateAssetMenu(fileName = "NewJob", menuName = "Outer Rim/Cards/Job")]
    public class JobCardData : CardData
    {
        public int CreditAdvance;
        public int RewardCredits;
        public int RewardFame;
        public bool RequiresSkill;
        public SkillType RequiredSkill;
        public int SkillDifficulty;
    }

    // ─── Ship (V2: new deck type) ───────────────────────────────────────────
    [CreateAssetMenu(fileName = "NewShip", menuName = "Outer Rim/Cards/Ship")]
    public class ShipCardData : CardData
    {
        public int Hyperdrive;
        public int MaxHull;
        public int CargoSlots;
        public int CrewSlots;
        public int ModSlots;
        public int ShipCombatValue;
        public int AttackDice;
    }

    // ─── Luxury ─────────────────────────────────────────────────────────────
    [CreateAssetMenu(fileName = "NewLuxury", menuName = "Outer Rim/Cards/Luxury")]
    public class LuxuryCardData : CardData
    {
        public int PremiumSellValue;
        public bool IsRestrictedItem;
        public int FameOnPurchase;
    }

    // ─── Encounter ──────────────────────────────────────────────────────────
    [CreateAssetMenu(fileName = "NewEncounter", menuName = "Outer Rim/Cards/Planet Encounter")]
    public class EncounterCardData : CardData
    {
        public string PlanetId;
        [TextArea(3,6)] public string EncounterText;
        public bool RequiresCheck;
        public SkillType CheckSkill;
        public int CheckDifficulty;
        public int SuccessCredits;
        public int SuccessFame;
        public int SuccessRepDelta;
        public FactionType SuccessRepFaction;
        public int FailureDamage;
        public int FailureCreditsLost;
        public int FailureRepDelta;
        public FactionType FailureRepFaction;
    }
}
