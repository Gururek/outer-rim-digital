// CombatResolver.cs — Handles all contested dice rolls (player vs patrol, skill checks).
// Runs exclusively on the server; results broadcast via ClientRpc.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class CombatResolver : NetworkBehaviour
    {
        public static CombatResolver Instance { get; private set; }

        [SerializeField] private DiceFaceDistribution standardDie;

        // ─── Structs ────────────────────────────────────────────────────────

        public struct CombatOutcome
        {
            public bool           PlayerWon;
            public int            DamageToEnemy;
            public int            DamageToPlayer;
            public DiceRollResult PlayerRoll;
            public DiceRollResult EnemyRoll;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // ─── Ship Combat ────────────────────────────────────────────────────

        /// <summary>
        /// Full combat sequence: rolls dice for both sides, applies damage,
        /// broadcasts the visual result to all clients.
        /// Crits deal +1 bonus damage on top of the margin of victory.
        /// </summary>
        public IEnumerator ResolveCombat(PlayerState player, PatrolEnemy enemy)
        {
            if (!IsServer) yield break;

            int playerDice = player.AttackDice.Value;
            int enemyDice  = enemy.AttackDice;

            var playerRoll = DiceRoller.Roll(playerDice, standardDie);
            var enemyRoll  = DiceRoller.Roll(enemyDice,  standardDie);

            bool playerWon = playerRoll.Hits > enemyRoll.Hits;
            int  margin    = Mathf.Abs(playerRoll.Hits - enemyRoll.Hits);

            var outcome = new CombatOutcome
            {
                PlayerWon      = playerWon,
                DamageToEnemy  = playerWon ? margin + playerRoll.Crits : 0,
                DamageToPlayer = playerWon ? 0 : margin + enemyRoll.Crits,
                PlayerRoll     = playerRoll,
                EnemyRoll      = enemyRoll
            };

            // Apply damage.
            if (outcome.DamageToPlayer > 0)
                player.TakeHullDamage(outcome.DamageToPlayer);

            if (outcome.DamageToEnemy >= enemy.HullPoints)
            {
                player.AddCredits(enemy.RewardCredits);
                player.AddFame(enemy.RewardFame);
                player.ModifyReputation(enemy.Faction, enemy.ReputationReward);
            }

            // Broadcast roll results to all clients for animation.
            BroadcastCombatResultClientRpc(
                player.OwnerClientId,
                playerRoll.ToIntArray(),
                enemyRoll.ToIntArray(),
                outcome.PlayerWon,
                outcome.DamageToPlayer,
                outcome.DamageToEnemy);

            yield return new WaitForSeconds(2.5f); // Wait for dice animation
            GameManager.Instance?.NotifyEncounterComplete();
        }

        [ClientRpc]
        private void BroadcastCombatResultClientRpc(
            ulong  playerClientId,
            int[]  playerFaces,
            int[]  enemyFaces,
            bool   playerWon,
            int    damageToPlayer,
            int    damageToEnemy)
        {
            Debug.Log($"[CombatResolver] Combat result — PlayerWon:{playerWon} DmgPlayer:{damageToPlayer} DmgEnemy:{damageToEnemy}");
            // Trigger dice roll animation on all clients' UI — Phase 4.
        }

        // ─── Skill Check ────────────────────────────────────────────────────

        /// <summary>
        /// Resolves a non-combat skill check (e.g., Encounter card).
        /// Returns true if player's hits meet or exceed the difficulty threshold.
        /// </summary>
        public bool ResolveSkillCheck(PlayerState player, SkillType skill, int difficulty, out DiceRollResult rollResult)
        {
            int numDice = player.GetSkillValue(skill);
            rollResult  = DiceRoller.Roll(numDice, standardDie);
            return rollResult.Hits >= difficulty;
        }
    }
}
