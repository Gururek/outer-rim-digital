// CombatResolver.cs — V2 per Outer Rim rulebooks
// Both sides roll. Both take the damage their opponent rolled.
// Crit = 2 damage, Hit = 1 damage. No margin of victory.
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class CombatResolver : NetworkBehaviour
    {
        public static CombatResolver Instance { get; private set; }

        [SerializeField] private DiceFaceDistribution standardDie;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        /// <summary>V2 combat: both sides roll, both take opponent's damage.</summary>
        public IEnumerator ResolveShipCombat(PlayerState player, int patrolDice, FactionType faction)
        {
            if (!IsServer) yield break;
            int playerDiceCount = player.ShipCombatValue.Value;

            var playerRoll = DiceRoller.Roll(playerDiceCount, standardDie);
            var patrolRoll = DiceRoller.Roll(patrolDice, standardDie);

            int damageToPatrol = playerRoll.Damage;  // V2: Hit=1, Crit=2
            int damageToPlayer = patrolRoll.Damage;

            player.TakeHullDamage(damageToPlayer);

            bool playerWon = damageToPatrol >= 3; // simplified: need 3+ damage to defeat patrol
            int credits = playerWon ? Random.Range(1000, 3000) : 0;
            int fame = playerWon ? (faction == FactionType.Imperial ? 2 : 1) : 0;

            if (playerWon)
            {
                player.AddCredits(credits);
                player.AddFame(fame);
                player.SetReputation(faction, ReputationStatus.Neutral); // lose negative rep
            }

            BroadcastResultClientRpc(player.OwnerClientId, playerRoll.ToIntArray(), patrolRoll.ToIntArray(),
                playerWon, damageToPlayer, credits, fame);

            yield return new WaitForSeconds(2.5f);
            GameManager.Instance?.NotifyEncounterComplete();
        }

        [ClientRpc]
        private void BroadcastResultClientRpc(ulong cid, int[] pFaces, int[] eFaces, bool won, int dmg, int cr, int fame)
        {
            Debug.Log($"[Combat] Player {cid}: won={won}, dmg={dmg}, cr={cr}, fame={fame}");
        }

        /// <summary>V2 skill test: always roll 2 dice. Skill level determines pass threshold.</summary>
        public DiceRollResult RollSkillTest(int skillLevel)
        {
            return DiceRoller.Roll(2, standardDie);
        }

        /// <summary>Check if a skill test passes based on V2 thresholds.</summary>
        public static bool SkillTestPasses(DiceRollResult roll, int skillLevel)
        {
            // Unskilled (0): need at least 1 Crit
            // Skilled (1): need at least 1 Hit OR Crit
            // Highly Skilled (2+): need at least 1 Focus, Hit, OR Crit
            return skillLevel switch
            {
                0 => roll.Crits >= 1,
                1 => roll.Hits >= 1 || roll.Crits >= 1,
                _ => roll.Focuses >= 1 || roll.Hits >= 1 || roll.Crits >= 1
            };
        }
    }
}
