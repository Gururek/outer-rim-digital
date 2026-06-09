// EncounterResolver.cs — V2: uses PatrolManager + CombatResolver per rulebooks
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

        // Contact tokens: nodeId -> databank card number
        private Dictionary<int, int> contactTokens = new();
        // Visual markers for contact tokens: nodeId -> GameObject
        private Dictionary<int, GameObject> tokenVisuals = new();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                PlaceContactTokens();
                PatrolManager.Instance?.PlacePatrols();
            }
        }

        private void PlaceContactTokens()
        {
            contactTokens.Clear();
            // Destroy any existing visuals
            foreach (var kvp in tokenVisuals)
                if (kvp.Value != null) Destroy(kvp.Value);
            tokenVisuals.Clear();

            if (MapManager.Instance == null || DataBankManager.Instance == null) return;

            // Place one contact per faction from the databank
            foreach (FactionType faction in new[] { FactionType.Hutt, FactionType.Syndicate, FactionType.Imperial, FactionType.Rebel })
            {
                var cards = DataBankManager.Instance.GetCardsByFaction(faction);
                if (cards.Count == 0) continue;

                // Find a planet of that faction
                var planetNodes = new List<int>();
                foreach (var node in MapManager.Instance.allNodes)
                    if (node.Type == MapNodeType.Planet && node.PlanetFactionType == faction)
                        planetNodes.Add(node.NodeId);

                if (planetNodes.Count > 0)
                {
                    int planetId = planetNodes[Random.Range(0, planetNodes.Count)];
                    var card = cards[Random.Range(0, cards.Count)];
                    contactTokens[planetId] = card.CardNumber;

                    // Create visual token on the planet
                    var planetNode = MapManager.Instance.nodeLookup.TryGetValue(planetId, out var n) ? n : null;
                    if (planetNode != null)
                    {
                        var tokenGo = new GameObject($"ContactToken_{card.CardNumber}");
                        var tokenVis = tokenGo.AddComponent<ContactTokenVisual>();
                        tokenVis.Initialize(card.CardNumber, card.ContactClass, planetNode.transform);
                        tokenVisuals[planetId] = tokenGo;
                    }
                }
            }
            Debug.Log($"[Encounter] Placed {contactTokens.Count} contact tokens.");
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

            // V2: Check for mandatory patrol (negative rep)
            var mandatoryPatrol = PatrolManager.Instance?.GetMandatoryPatrol(player);
            if (mandatoryPatrol != null)
            {
                yield return StartCoroutine(HandlePatrolCombat(player, mandatoryPatrol));
            }
            else if (contactTokens.ContainsKey(node.NodeId))
            {
                int cardNum = contactTokens[node.NodeId];
                contactTokens.Remove(node.NodeId);

                // Remove visual token
                if (tokenVisuals.TryGetValue(node.NodeId, out var visGo))
                {
                    if (visGo != null)
                    {
                        var vis = visGo.GetComponent<ContactTokenVisual>();
                        if (vis != null) { vis.Reveal(); Destroy(visGo, 2f); }
                        else Destroy(visGo);
                    }
                    tokenVisuals.Remove(node.NodeId);
                }

                var card = DataBankManager.Instance?.GetCard(cardNum);
                player.AddCredits(card?.BountyReward ?? 1000);
                player.AddFame(1);
                NotifyResultClientRpc(player.OwnerClientId, $"Contact found: {card?.CardName ?? "Unknown"}! +{card?.BountyReward ?? 1000}cr +1 Fame.");
            }
            else if (node.Type == MapNodeType.NavPoint || node.Type == MapNodeType.Maelstrom)
            {
                yield return StartCoroutine(HandleNavPointEncounter(player, node));
            }
            else if (node.Type == MapNodeType.Planet)
            {
                HandlePlanetEncounter(player, node);
            }

            yield return new WaitForSeconds(combatDelay);
            GameManager.Instance?.NotifyEncounterComplete();
        }

        private IEnumerator HandlePatrolCombat(PlayerState player, Patrol patrol)
        {
            NotifyResultClientRpc(player.OwnerClientId, $"Forced patrol encounter: {patrol.Faction} L{(int)patrol.Level}!");

            if (patrol.IsInvulnerable)
            {
                // V2: Level 4 — always defeats player
                player.TakeHullDamage(5);
                NotifyResultClientRpc(player.OwnerClientId, $"Level-4 {patrol.Faction} patrol is INVULNERABLE! Took 5 damage.");
                yield break;
            }

            if (CombatResolver.Instance != null)
            {
                yield return StartCoroutine(CombatResolver.Instance.ResolveShipCombat(player, patrol.CombatDice, patrol.Faction));
                PatrolManager.Instance?.DefeatPatrol(patrol, player);
            }
        }

        private IEnumerator HandleNavPointEncounter(PlayerState player, MapNode node)
        {
            var patrols = PatrolManager.Instance?.GetPatrolsAtNode(node.NodeId);
            if (patrols != null && patrols.Count > 0)
            {
                var patrol = patrols[0];
                ReputationStatus rep = player.GetReputation(patrol.Faction);

                // V2: Negative rep = forced encounter
                if (rep == ReputationStatus.Negative)
                {
                    yield return StartCoroutine(HandlePatrolCombat(player, patrol));
                    yield break;
                }

                // Base detection 2/6, positive rep reduces to 0
                int detection = rep == ReputationStatus.Positive ? 0 : 2;
                if (Random.Range(0, 6) < detection)
                {
                    yield return StartCoroutine(HandlePatrolCombat(player, patrol));
                    yield break;
                }
                NotifyResultClientRpc(player.OwnerClientId, $"Slipped past {patrol.Faction} patrol at {node.NodeName}.");
                yield break;
            }
            NotifyResultClientRpc(player.OwnerClientId, $"No patrols at {node.NodeName}.");
        }

        private void HandlePlanetEncounter(PlayerState player, MapNode node)
        {
            if (DeckManager.Instance != null)
            {
                var card = DeckManager.Instance.DrawPlanetEncounterCard(node.NodeName);
                if (card != null)
                {
                    ResolveEncounterCard(player, card);
                    return;
                }
            }
            // Fallback skill test
            int hits = 0;
            for (int i = 0; i < 2; i++) if (Random.Range(0, 6) >= 3) hits++;
            if (hits >= 1) { player.AddCredits(Random.Range(1000, 3000)); NotifyResultClientRpc(player.OwnerClientId, $"Encounter success at {node.NodeName}."); }
            else NotifyResultClientRpc(player.OwnerClientId, $"Nothing at {node.NodeName}.");
        }

        private void ResolveEncounterCard(PlayerState player, EncounterCardData card)
        {
            if (!card.RequiresCheck)
            { player.AddCredits(card.SuccessCredits); player.AddFame(card.SuccessFame); NotifyResultClientRpc(player.OwnerClientId, card.EncounterText); return; }

            int hits = 0;
            for (int i = 0; i < 2; i++) if (Random.Range(0, 6) >= 3) hits++;
            if (hits >= card.CheckDifficulty)
            {
                player.AddCredits(card.SuccessCredits); player.AddFame(card.SuccessFame);
                NotifyResultClientRpc(player.OwnerClientId, $"{card.CardName}: PASS!");
            }
            else
            {
                if (card.FailureDamage > 0) player.TakeHullDamage(card.FailureDamage);
                NotifyResultClientRpc(player.OwnerClientId, $"{card.CardName}: FAIL.");
            }
        }

        private MapNode GetPlayerNode(PlayerState player)
        {
            if (MapManager.Instance == null) return null;
            return MapManager.Instance.nodeLookup.TryGetValue(player.CurrentNodeId.Value, out var n) ? n : null;
        }

        [ClientRpc]
        private void NotifyResultClientRpc(ulong cid, string msg) => Debug.Log($"[Encounter] P{cid}: {msg}");
    }
}
