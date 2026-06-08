// EncounterResolver.cs — Auto-resolves encounters during Encounter Phase.
// Runs server-side coroutine with patrol detection, planet encounters, dice combat.
using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace OuterRim
{
    public class EncounterResolver : NetworkBehaviour
    {
        public static EncounterResolver Instance { get; private set; }

        [SerializeField] private float encounterDelay = 0.3f;
        [SerializeField] private float combatDelay = 2.5f;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        /// <summary>Entry point — called from GameManager during EncounterPhase.</summary>
        public void ResolveEncounter(PlayerState player)
        {
            if (!IsServer) return;
            StartCoroutine(ResolveEncounterCoroutine(player));
        }

        private IEnumerator ResolveEncounterCoroutine(PlayerState player)
        {
            yield return new WaitForSeconds(encounterDelay);

            string result;

            // Check for patrol at current node
            var node = GetPlayerNode(player);
            if (node != null && node.Type == MapNodeType.NavPoint)
            {
                // NavPoint: check for patrol detection based on faction rep
                var faction = node.PlanetFactionType;
                int rep = player.GetReputation(faction);
                int detectionChance = rep < 0 ? Mathf.Abs(rep) * 20 : 0; // -1 rep = 20%, -2 = 40%, etc.

                if (Random.Range(0, 100) < detectionChance)
                {
                    result = ResolvePatrolCombat(player, faction);
                }
                else
                {
                    result = $"No patrol at {node.NodeName}. Safe passage.";
                }
            }
            else if (node != null && node.Type == MapNodeType.Planet)
            {
                // Planet: resolve a basic planet encounter
                result = ResolvePlanetEncounter(player, node);
            }
            else
            {
                result = "No encounter.";
            }

            NotifyEncounterResultClientRpc(player.OwnerClientId, result);

            yield return new WaitForSeconds(combatDelay);
            GameManager.Instance?.NotifyEncounterComplete();
        }

        private string ResolvePatrolCombat(PlayerState player, FactionType faction)
        {
            // Simple patrol combat: roll dice
            int playerDice = player.AttackDice.Value;
            int patrolDice = 2;
            int patrolHits = 0;
            int playerHits = 0;

            for (int i = 0; i < playerDice; i++)
                if (Random.Range(0, 6) >= 3) playerHits++;
            for (int i = 0; i < patrolDice; i++)
                if (Random.Range(0, 6) >= 3) patrolHits++;

            if (playerHits > patrolHits)
            {
                int fameGain = Random.Range(1, 3);
                int creditGain = Random.Range(500, 2000);
                player.AddFame(fameGain);
                player.AddCredits(creditGain);
                player.ModifyReputation(faction, 1);
                return $"Patrol defeated! +{fameGain} Fame, +{creditGain} Credits.";
            }
            else
            {
                int damage = patrolHits - playerHits;
                player.TakeHullDamage(damage);
                player.ModifyReputation(faction, -1);
                return $"Patrol engagement lost! Took {damage} damage.";
            }
        }

        private string ResolvePlanetEncounter(PlayerState player, MapNode node)
        {
            // Simple planet encounter: skill check
            int skillValue = player.GetSkillValue(SkillType.Cunning);
            int diceCount = skillValue;
            int hits = 0;

            for (int i = 0; i < diceCount; i++)
                if (Random.Range(0, 6) >= 3) hits++;

            if (hits >= 1)
            {
                int creditGain = Random.Range(1000, 3000);
                player.AddCredits(creditGain);
                return $"Successful encounter at {node.NodeName}! +{creditGain} Credits.";
            }
            else
            {
                return $"Nothing of interest at {node.NodeName}.";
            }
        }

        private MapNode GetPlayerNode(PlayerState player)
        {
            if (MapManager.Instance == null) return null;
            return MapManager.Instance.nodeLookup.TryGetValue(player.CurrentNodeId.Value, out var node) ? node : null;
        }

        [ClientRpc]
        private void NotifyEncounterResultClientRpc(ulong clientId, string result)
        {
            Debug.Log($"[EncounterResolver] Player {clientId}: {result}");
        }
    }
}
