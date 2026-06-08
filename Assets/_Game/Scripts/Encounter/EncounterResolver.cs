// EncounterResolver.cs — Encounter phase per Outer Rim TDD Phase 3
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace OuterRim
{
    public class EncounterResolver : NetworkBehaviour
    {
        public static EncounterResolver Instance { get; private set; }

        [SerializeField] private float encounterDelay = 0.3f;
        [SerializeField] private float combatDelay = 2.5f;

        // Contact tokens: which nodes have face-down tokens
        private Dictionary<int, int> contactTokens = new(); // nodeId -> cardId

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) PlaceContactTokens();
        }

        /// <summary>Seed contact tokens on random planets (Outer Rim setup rule).</summary>
        private void PlaceContactTokens()
        {
            contactTokens.Clear();
            if (MapManager.Instance == null) return;
            
            // Place 1 contact token per faction on a random planet of that faction
            var factionPlanets = new Dictionary<FactionType, List<int>>();
            foreach (var node in MapManager.Instance.allNodes)
            {
                if (node.Type == MapNodeType.Planet)
                {
                    if (!factionPlanets.ContainsKey(node.PlanetFactionType))
                        factionPlanets[node.PlanetFactionType] = new List<int>();
                    factionPlanets[node.PlanetFactionType].Add(node.NodeId);
                }
            }

            int tokenId = 100;
            foreach (var kvp in factionPlanets)
            {
                if (kvp.Value.Count == 0) continue;
                int planetId = kvp.Value[Random.Range(0, kvp.Value.Count)];
                contactTokens[planetId] = tokenId++;
            }
            Debug.Log($"[Encounter] Placed {contactTokens.Count} contact tokens on planets.");
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
                NotifyResultClientRpc(player.OwnerClientId, "No encounter.");
                yield return new WaitForSeconds(combatDelay);
                GameManager.Instance?.NotifyEncounterComplete();
                yield break;
            }

            string result;

            // Contact token check (Outer Rim: reveal facedown contact on planet)
            if (contactTokens.ContainsKey(node.NodeId))
            {
                int cardId = contactTokens[node.NodeId];
                result = ResolveContactToken(player, node, cardId);
            }
            else if (node.Type == MapNodeType.NavPoint)
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

            NotifyResultClientRpc(player.OwnerClientId, result);
            yield return new WaitForSeconds(combatDelay);
            GameManager.Instance?.NotifyEncounterComplete();
        }

        /// <summary>Contact token: find matching bounty or fight contact.</summary>
        private string ResolveContactToken(PlayerState player, MapNode node, int cardId)
        {
            contactTokens.Remove(node.NodeId);
            Debug.Log($"[Encounter] Contact token revealed at {node.NodeName}!");

            // Simplified: gain credits + fame for finding a contact
            player.AddCredits(1000);
            player.AddFame(1);
            return $"Contact found at {node.NodeName}! +1000cr +1 Fame.";
        }

        private string ResolvePatrolEncounter(PlayerState player, MapNode node)
        {
            FactionType faction = node.PlanetFactionType;
            int rep = player.GetReputation(faction);
            int detectionBase = 2;
            int detectionChance = Mathf.Clamp(detectionBase - rep, 0, 5);
            int roll = Random.Range(0, 6);

            if (roll >= detectionChance)
                return $"Slipped past {faction} patrol at {node.NodeName}.";

            int patrolDice = faction == FactionType.Authority ? 3 : 2;
            int playerDice = player.AttackDice.Value;
            int patrolHits = 0, playerHits = 0;
            for (int i = 0; i < patrolDice; i++) if (Random.Range(0, 6) >= 3) patrolHits++;
            for (int i = 0; i < playerDice; i++) if (Random.Range(0, 6) >= 3) playerHits++;

            if (playerHits > patrolHits)
            {
                int fameGain = faction == FactionType.Authority ? 2 : 1;
                player.AddFame(fameGain);
                player.AddCredits(Random.Range(1000, 3000));
                player.ModifyReputation(faction, 1);
                return $"Defeated {faction} patrol! +{fameGain} Fame.";
            }
            else
            {
                int damage = patrolHits - playerHits;
                player.TakeHullDamage(damage);
                player.ModifyReputation(faction, -1);
                return $"Lost to {faction} patrol. Took {damage} damage.";
            }
        }

        /// <summary>Planet encounter: draw card from DeckManager if available, else skill check.</summary>
        private string ResolvePlanetEncounter(PlayerState player, MapNode node)
        {
            // Try drawing from the encounter deck for this planet
            if (DeckManager.Instance != null)
            {
                var card = DeckManager.Instance.DrawPlanetEncounterCard(node.NodeName);
                if (card != null)
                {
                    return ResolveEncounterCard(player, card);
                }
            }

            // Fallback: generic skill check
            int hits = 0;
            for (int i = 0; i < player.GetSkillValue(SkillType.Cunning); i++)
                if (Random.Range(0, 6) >= 3) hits++;

            if (hits >= 2)
            {
                player.AddCredits(Random.Range(1000, 3000));
                return $"Encounter at {node.NodeName}: success!";
            }
            return $"Nothing of interest at {node.NodeName}.";
        }

        /// <summary>Resolve a specific EncounterCardData drawn from the deck.</summary>
        private string ResolveEncounterCard(PlayerState player, EncounterCardData card)
        {
            Debug.Log($"[Encounter] Drawn card: {card.CardName} at {card.PlanetId}");

            if (!card.RequiresCheck)
            {
                player.AddCredits(card.SuccessCredits);
                player.AddFame(card.SuccessFame);
                return card.EncounterText + $" +{card.SuccessCredits}cr +{card.SuccessFame} fame";
            }

            int dice = player.GetSkillValue(card.CheckSkill);
            int hits = 0;
            for (int i = 0; i < dice; i++)
                if (Random.Range(0, 6) >= 3) hits++;

            if (hits >= card.CheckDifficulty)
            {
                player.AddCredits(card.SuccessCredits);
                player.AddFame(card.SuccessFame);
                if (card.SuccessRepDelta != 0)
                    player.ModifyReputation(card.SuccessRepFaction, card.SuccessRepDelta);
                return $"{card.CardName}: PASS ({card.CheckSkill} {hits}/{card.CheckDifficulty})! +{card.SuccessCredits}cr +{card.SuccessFame} fame";
            }
            else
            {
                if (card.FailureDamage > 0) player.TakeHullDamage(card.FailureDamage);
                if (card.FailureCreditsLost > 0 && player.Credits.Value >= card.FailureCreditsLost)
                    player.SpendCredits(card.FailureCreditsLost);
                if (card.FailureRepDelta != 0)
                    player.ModifyReputation(card.FailureRepFaction, card.FailureRepDelta);
                return $"{card.CardName}: FAIL ({card.CheckSkill} {hits}/{card.CheckDifficulty}). {card.FailureDamage} dmg";
            }
        }

        private MapNode GetPlayerNode(PlayerState player)
        {
            if (MapManager.Instance == null) return null;
            return MapManager.Instance.nodeLookup.TryGetValue(player.CurrentNodeId.Value, out var node) ? node : null;
        }

        [ClientRpc]
        private void NotifyResultClientRpc(ulong clientId, string result)
        {
            Debug.Log($"[Encounter] Player {clientId}: {result}");
        }
    }
}
