// PatrolEnemy.cs — Plain data class for patrol enemies encountered at faction-controlled nodes.
using System;

namespace OuterRim
{
    [Serializable]
    public class PatrolEnemy
    {
        public string      Name;
        public FactionType Faction;
        public int         AttackDice;
        public int         HullPoints;
        public int         RewardCredits;
        public int         RewardFame;
        public int         ReputationReward;
    }
}
