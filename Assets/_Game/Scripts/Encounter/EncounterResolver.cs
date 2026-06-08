// EncounterResolver.cs — Auto-resolves encounters per Outer Rim rules
// Patrol detection, stealth tests, planet encounters, contact tokens
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

        public void ResolveEncounter(PlayerState player)
        {
            if (!IsServer) return;
            StartCoroutine(ResolveEncounterCoroutine(player));
        }

        private IEnumerator ResolveEncounterCoroutine(PlayerState player)
        {
            yield return new WaitForSeconds(encounterDelay);

            var node = GetPlayerNode(player);
            if (node == null)
            {
                NotifyEncounterResultClientRpc(player.OwnerClientId, "No encounter.");
                yield return new WaitForSeconds(combatDelay);
                GameManager.Instance?.NotifyEncounterComplete();
                yield break;
            }

            string result;

            // ─── Outer Rim Rule: Patrol Detection ─────────────────────
            // When on a NavPoint or moving into a patrol space, check detection.
            // Base detection chance depends on reputation with the patrol's faction.
            // Negative rep: higher detection. Positive rep: lower detection.
            if (node.Type == MapNodeType.NavPoint)
            {
                result = ResolvePatrolEncounter(player, node);
            }
            else if (node.Type == MapNodeType.Planet)
            {
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

        /// <summary>
        /// Outer Rim patrol rules:
        /// - Base detection chance: 2 in 6 (33%)
        /// - Each negative reputation: +1 in 6 detection
        /// - Each positive reputation: -1 in 6 detection (minimum 0)
        /// - If detected: fight ship combat against patrol
        /// - Patrol strength depends on faction
        /// </summary>
        private string ResolvePatrolEncounter(PlayerState player, MapNode node)
        {
            FactionType faction = node.PlanetFactionType;
            int rep = player.GetReputation(faction);
            
            // Detection chance (Outer Rim: base 2/6, modified by reputation)
            int detectionBase = 2;
            int detectionMod = -rep; // negative rep increases detection, positive decreases
            int detectionChance = Mathf.Clamp(detectionBase + detectionMod, 0, 5);
            int roll = Random.Range(0, 6);

            Debug.Log($"[Encounter] Patrol detection at {node.NodeName}: faction={faction}, rep={rep}, detection={detectionChance}/6, roll={roll}");

            if (roll >= detectionChance)
            {
                return $"Slipped past {faction} patrol at {node.NodeName}. (detection {detectionChance}/6, rolled {roll})";
            }

            // Detected! Fight patrol
            return FightPatrol(player, faction);
        }

        private string FightPatrol(PlayerState player, FactionType faction)
        {
            // Patrol strength by faction (Outer Rim rules)
            int patrolDice = faction switch
            {
                FactionType.Authority => 3,  // Imperial patrols are stronger
                FactionType.Syndicate => 2,
                FactionType.Hutts     => 2,
                FactionType.Rebels    => 2,
                _ => 2
            };

            int playerDice = player.AttackDice.Value;
            int patrolHits = 0, playerHits = 0;

            for (int i = 0; i < patrolDice; i++)
                if (Random.Range(0, 6) >= 3) patrolHits++;
            for (int i = 0; i < playerDice; i++)
                if (Random.Range(0, 6) >= 3) playerHits++;

            if (playerHits > patrolHits)
            {
                int fameGain = faction == FactionType.Authority ? 2 : 1;
                int creditGain = Random.Range(1000, 3000);
                player.AddFame(fameGain);
                player.AddCredits(creditGain);
                player.ModifyReputation(faction, 1);
                return $"Defeated {faction} patrol! +{fameGain} Fame, +{creditGain} Credits.";
            }
            else
            {
                int damage = patrolHits - playerHits;
                player.TakeHullDamage(damage);
                player.ModifyReputation(faction, -1);
                return $"Lost to {faction} patrol! Took {damage} damage. Reputation -1.";
            }
        }

        private string ResolvePlanetEncounter(PlayerState player, MapNode node)
        {
            // Outer Rim: on planets, draw an encounter card for that planet type.
            // Simplified: skill check with Cunning
            int diceCount = player.GetSkillValue(SkillType.Cunning);
            int hits = 0;
            for (int i = 0; i < diceCount; i++)
                if (Random.Range(0, 6) >= 3) hits++;

            if (hits >= 2)
            {
                int creditGain = Random.Range(1000, 3000);
                player.AddCredits(creditGain);
                return $"Successful encounter at {node.NodeName}! +{creditGain} Credits.";
            }
            else
            {
                return $"Nothing of interest at {node.NodeName}. (needed 2 hits, got {hits})";
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
            Debug.Log($"[Encounter] Player {clientId}: {result}");
        }
    }
}
