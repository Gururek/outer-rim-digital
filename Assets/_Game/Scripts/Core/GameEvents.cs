// GameEvents.cs — V2: event payloads
namespace OuterRim
{
    public struct CombatResult
    {
        public bool PlayerWon;
        public int DamageDealt;
        public int DamageTaken;
        public int CreditsGained;
        public int FameGained;
    }

    public struct SkillTestResult
    {
        public bool Passed;
        public int Hits;
        public int Crits;
        public SkillType Skill;
        public int Difficulty;
    }
}
