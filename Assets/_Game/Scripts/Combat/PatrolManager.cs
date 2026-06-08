// PatrolManager.cs — V2: 4-faction patrol system per Outer Rim rulebooks
// Handles patrol placement, movement, detection, and level-4 invulnerability.
using System.Collections.Generic;
using UnityEngine;

namespace OuterRim
{
    /// <summary>Runtime patrol instance on the map.</summary>
    [System.Serializable]
    public class Patrol
    {
        public int PatrolId;
        public FactionType Faction;
        public PatrolLevel Level;
        public int CurrentNodeId;
        public bool IsDefeated;

        // V2 rewards per level
        public int CreditReward => Level switch
        {
            PatrolLevel.Level1 => 1000,
            PatrolLevel.Level2 => 0,
            PatrolLevel.Level3 => 0,
            PatrolLevel.Level4 => 0, // invulnerable
            _ => 0
        };

        public int FameReward => Level switch
        {
            PatrolLevel.Level1 => 0,
            PatrolLevel.Level2 => 1,
            PatrolLevel.Level3 => 2,
            PatrolLevel.Level4 => 0,
            _ => 0
        };

        public int CombatDice => Level switch
        {
            PatrolLevel.Level1 => 2,
            PatrolLevel.Level2 => 2,
            PatrolLevel.Level3 => 3,
            PatrolLevel.Level4 => 99, // invulnerable — will always defeat player
            _ => 2
        };

        public bool IsInvulnerable => Level == PatrolLevel.Level4;
    }

    /// <summary>
    /// Server-authoritative patrol manager.
    /// Patrols move after market searches. Players encounter them on navpoints.
    /// </summary>
    public class PatrolManager : MonoBehaviour
    {
        public static PatrolManager Instance { get; private set; }

        [Header("Patrol Config")]
        [SerializeField] private int patrolsPerFaction = 4; // levels 1-4

        private List<Patrol> activePatrols = new();
        private int nextPatrolId = 100;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        /// <summary>Place all patrols on faction navpoints at game start.</summary>
        public void PlacePatrols()
        {
            activePatrols.Clear();
            var factionNodes = GetFactionNavPoints();

            foreach (var kvp in factionNodes)
            {
                FactionType faction = kvp.Key;
                var nodes = kvp.Value;
                if (nodes.Count == 0) continue;

                for (int level = 1; level <= patrolsPerFaction; level++)
                {
                    int nodeIdx = Random.Range(0, nodes.Count);
                    var patrol = new Patrol
                    {
                        PatrolId = nextPatrolId++,
                        Faction = faction,
                        Level = (PatrolLevel)level,
                        CurrentNodeId = nodes[nodeIdx],
                        IsDefeated = false
                    };
                    activePatrols.Add(patrol);
                }
            }

            Debug.Log($"[PatrolManager] Placed {activePatrols.Count} patrols across {factionNodes.Count} factions.");
        }

        /// <summary>Get all patrols currently at a given node.</summary>
        public List<Patrol> GetPatrolsAtNode(int nodeId)
        {
            var result = new List<Patrol>();
            foreach (var p in activePatrols)
                if (!p.IsDefeated && p.CurrentNodeId == nodeId)
                    result.Add(p);
            return result;
        }

        /// <summary>Check if any patrol at this node forces an encounter (negative rep).</summary>
        public Patrol GetMandatoryPatrol(PlayerState player)
        {
            int nodeId = player.CurrentNodeId.Value;
            foreach (var p in activePatrols)
            {
                if (!p.IsDefeated && p.CurrentNodeId == nodeId)
                {
                    if (player.GetReputation(p.Faction) == ReputationStatus.Negative)
                        return p;
                }
            }
            return null;
        }

        /// <summary>Defeat a patrol: mark defeated, grant rewards, lose rep, spawn replacement.</summary>
        public void DefeatPatrol(Patrol patrol, PlayerState player)
        {
            if (patrol.IsInvulnerable)
            {
                Debug.Log($"[PatrolManager] Level-4 patrol {patrol.PatrolId} is invulnerable!");
                return;
            }

            patrol.IsDefeated = true;
            player.AddCredits(patrol.CreditReward);
            player.AddFame(patrol.FameReward);
            player.SetReputation(patrol.Faction, ReputationStatus.Negative);

            Debug.Log($"[PatrolManager] Patrol {patrol.PatrolId} ({patrol.Faction} L{patrol.Level}) defeated by Player {player.OwnerClientId}");

            // Spawn replacement patrol at a random faction navpoint
            SpawnReplacement(patrol.Faction, patrol.Level);
        }

        private void SpawnReplacement(FactionType faction, PatrolLevel level)
        {
            var nodes = GetFactionNavPoints().GetValueOrDefault(faction, new List<int>());
            if (nodes.Count == 0) return;

            int nodeId = nodes[Random.Range(0, nodes.Count)];
            var newPatrol = new Patrol
            {
                PatrolId = nextPatrolId++,
                Faction = faction,
                Level = level,
                CurrentNodeId = nodeId
            };
            activePatrols.Add(newPatrol);
            Debug.Log($"[PatrolManager] Spawned replacement patrol {newPatrol.PatrolId} ({faction} L{level}) at node {nodeId}");
        }

        /// <summary>Move a patrol to an adjacent node (called after market search).</summary>
        public void MovePatrol(Patrol patrol)
        {
            if (patrol.IsDefeated || MapManager.Instance == null) return;

            var adjacent = MapManager.Instance.GetReachableNodes(patrol.CurrentNodeId, 1);
            if (adjacent.Count <= 1) return; // nowhere to go

            // Pick random adjacent (excluding current position)
            var candidates = new List<int>();
            foreach (var n in adjacent)
                if (n != patrol.CurrentNodeId) candidates.Add(n);

            if (candidates.Count > 0)
            {
                int dest = candidates[Random.Range(0, candidates.Count)];
                patrol.CurrentNodeId = dest;
                Debug.Log($"[PatrolManager] Patrol {patrol.PatrolId} moved to node {dest}");
            }
        }

        public List<Patrol> GetAllPatrols() => activePatrols;

        private Dictionary<FactionType, List<int>> GetFactionNavPoints()
        {
            var result = new Dictionary<FactionType, List<int>>();
            if (MapManager.Instance == null) return result;

            foreach (var node in MapManager.Instance.allNodes)
            {
                if (node.Type == MapNodeType.NavPoint && node.PlanetFactionType != FactionType.None)
                {
                    if (!result.ContainsKey(node.PlanetFactionType))
                        result[node.PlanetFactionType] = new List<int>();
                    result[node.PlanetFactionType].Add(node.NodeId);
                }
            }

            // Fallback: use planet nodes if no navpoints
            foreach (var kvp in result)
            {
                if (kvp.Value.Count > 0) continue;
                foreach (var node in MapManager.Instance.allNodes)
                {
                    if (node.PlanetFactionType == kvp.Key)
                        kvp.Value.Add(node.NodeId);
                }
            }

            return result;
        }
    }
}
