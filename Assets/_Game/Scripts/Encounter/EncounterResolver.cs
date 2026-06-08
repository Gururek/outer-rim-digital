// EncounterResolver.cs — V2 per Outer Rim rulebooks
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
        private Dictionary<int, int> contactTokens = new();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) PlaceContactTokens();
        }

        private void PlaceContactTokens()
        {
            contactTokens.Clear();
            if (MapManager.Instance == null) return;
            var factionPlanets = new Dictionary<FactionType, List<int>>();
            foreach (var node in MapManager.Instance.allNodes)
            {
                if (node.Type == MapNodeType.Planet && node.PlanetFactionType != FactionType.None)
                {
                    if (!factionPlanets.ContainsKey(node.PlanetFactionType))
                        factionPlanets[node.PlanetFactionType] = new List<int>();
                    factionPlanets[node.PlanetFactionType].Add(node.NodeId);
                }
            }
            int tid = 100;
            foreach (var kvp in factionPlanets)
            {
                if (kvp.Value.Count == 0) continue;
                contactTokens[kvp.Value[Random.Range(0, kvp.Value.Count)]] = tid++;
            }
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
            if (contactTokens.ContainsKey(node.NodeId))
            {
                contactTokens.Remove(node.NodeId);
                player.AddCredits(1000); player.AddFame(1);
                result = $"Contact found at {node.NodeName}! +1000cr +1 Fame.";
            }
            else if (node.Type == MapNodeType.NavPoint || node.Type == MapNodeType.Maelstrom)
            {
                result = ResolvePatrolEncounter(player, node);
            }
            else
            {
                result = ResolvePlanetEncounter(player, node);
            }

            NotifyResultClientRpc(player.OwnerClientId, result);
            yield return new WaitForSeconds(combatDelay);
            GameManager.Instance?.NotifyEncounterComplete();
        }

        private string ResolvePatrolEncounter(PlayerState player, MapNode node)
        {
            FactionType faction = node.PlanetFactionType;
            ReputationStatus rep = player.GetReputation(faction);

            // V2: Negative rep = forced patrol encounter (auto-detect)
            if (rep == ReputationStatus.Negative)
            {
                int patrolDice = faction == FactionType.Imperial ? 3 : 2;
                if (CombatResolver.Instance != null)
                    StartCoroutine(CombatResolver.Instance.ResolveShipCombat(player, patrolDice, faction));
                return $"Forced patrol encounter ({faction}) — negative reputation!";
            }

            // Base detection 2/6, positive rep reduces
            int detection = rep == ReputationStatus.Positive ? 0 : 2;
            if (Random.Range(0, 6) >= detection)
                return $"Slipped past {faction} patrol at {node.NodeName}.";

            int pd = faction == FactionType.Imperial ? 3 : 2;
            if (CombatResolver.Instance != null)
                StartCoroutine(CombatResolver.Instance.ResolveShipCombat(player, pd, faction));
            return $"Patrol detected at {node.NodeName}! Fighting {faction} patrol.";
        }

        private string ResolvePlanetEncounter(PlayerState player, MapNode node)
        {
            if (DeckManager.Instance != null)
            {
                var card = DeckManager.Instance.DrawPlanetEncounterCard(node.NodeName);
                if (card != null) return ResolveEncounterCard(player, card);
            }
            int hits = 0;
            for (int i = 0; i < 2; i++) if (Random.Range(0, 6) >= 3) hits++;
            if (hits >= 1) { player.AddCredits(Random.Range(1000, 3000)); return $"Encounter at {node.NodeName}: success!"; }
            return $"Nothing of interest at {node.NodeName}.";
        }

        private string ResolveEncounterCard(PlayerState player, EncounterCardData card)
        {
            if (!card.RequiresCheck)
            {
                player.AddCredits(card.SuccessCredits); player.AddFame(card.SuccessFame);
                return card.EncounterText;
            }
            int hits = 0;
            for (int i = 0; i < 2; i++) if (Random.Range(0, 6) >= 3) hits++;
            bool pass = hits >= card.CheckDifficulty;
            if (pass)
            {
                player.AddCredits(card.SuccessCredits); player.AddFame(card.SuccessFame);
                if (card.SuccessRepDelta != 0) player.SetReputation(card.SuccessRepFaction, card.SuccessRepDelta > 0 ? ReputationStatus.Positive : ReputationStatus.Negative);
                return $"{card.CardName}: PASS!";
            }
            if (card.FailureDamage > 0) player.TakeHullDamage(card.FailureDamage);
            return $"{card.CardName}: FAIL. {card.FailureDamage} dmg.";
        }

        private MapNode GetPlayerNode(PlayerState player)
        {
            if (MapManager.Instance == null) return null;
            return MapManager.Instance.nodeLookup.TryGetValue(player.CurrentNodeId.Value, out var n) ? n : null;
        }

        [ClientRpc]
        private void NotifyResultClientRpc(ulong cid, string r) => Debug.Log($"[Encounter] P{cid}: {r}");
    }
}
