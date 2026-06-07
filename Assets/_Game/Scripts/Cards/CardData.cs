// CardData.cs — ScriptableObject hierarchy for all market and encounter cards.
using UnityEngine;

namespace OuterRim
{
    // ─── Base Card ──────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewCard", menuName = "Outer Rim/Cards/Base Card")]
    public class CardData : ScriptableObject
    {
        [Header("Identity")]
        public int            CardId;
        public string         CardName;
        [TextArea(2, 4)]
        public string         FlavorText;
        public Sprite         CardArt;
        public MarketDeckType DeckType;

        [Header("Market")]
        public int BuyCost;
        public int SellValue;

        [Header("Effect")]
        [TextArea(2, 5)]
        public string EffectDescription;
    }

    // ─── Bounty ─────────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewBounty", menuName = "Outer Rim/Cards/Bounty")]
    public class BountyCardData : CardData
    {
        [Header("Bounty Details")]
        public string      TargetName;
        public string      DeliveryPlanetId;
        public FactionType IssuingFaction;
        public int         RewardCredits;
        public int         RewardFame;
        public bool        RequiresPiloting;
        public int         PilotingDifficulty;
    }

    // ─── Cargo ──────────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewCargo", menuName = "Outer Rim/Cards/Cargo")]
    public class CargoCardData : CardData
    {
        [Header("Cargo Details")]
        public string DeliveryPlanetId;
        public int    DeliveryReward;
        public bool   IsIllegal;
        public int    IllegalFameReward;
    }

    // ─── Gear ───────────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewGear", menuName = "Outer Rim/Cards/Gear")]
    public class GearCardData : CardData
    {
        [Header("Gear Stats")]
        public SkillType AffectedSkill;
        public int       SkillBonus;
        public bool      IsSingleUse;
        public string    ActivationCondition;
    }

    // ─── Mod ────────────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewMod", menuName = "Outer Rim/Cards/Mod")]
    public class ModCardData : CardData
    {
        [Header("Ship Mod Stats")]
        public int    SpeedBonus;
        public int    HullBonus;
        public int    ExtraCargoSlots;
        public int    ExtraCrewSlots;
        public int    AttackDiceBonus;
        public string SpecialRule;
    }

    // ─── Job ────────────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewJob", menuName = "Outer Rim/Cards/Job")]
    public class JobCardData : CardData
    {
        [Header("Job Details")]
        public string OriginPlanetId;
        public string DestinationPlanetId;
        public int    CreditAdvance;
        public int    RewardCredits;
        public int    RewardFame;
        public bool   RequiresCunning;
        public int    CunningDifficulty;
    }

    // ─── Luxury ─────────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewLuxury", menuName = "Outer Rim/Cards/Luxury")]
    public class LuxuryCardData : CardData
    {
        [Header("Luxury Details")]
        public string PreferredBuyPlanetId;
        public string PremiumSellPlanetId;
        public int    PremiumSellValue;
        public bool   IsRestrictedItem;
    }

    // ─── Encounter ──────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewEncounter", menuName = "Outer Rim/Cards/Planet Encounter")]
    public class EncounterCardData : CardData
    {
        [Header("Encounter")]
        public string PlanetId;
        [TextArea(3, 6)]
        public string EncounterText;

        [Header("Skill Check")]
        public bool      RequiresCheck;
        public SkillType CheckSkill;
        public int       CheckDifficulty;

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
}
