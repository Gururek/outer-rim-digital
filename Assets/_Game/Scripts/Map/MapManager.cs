using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace OuterRim
{
    public class MapManager : NetworkBehaviour
    {
        private static MapManager _instance;
        public static MapManager Instance => _instance;
        public List<MapNode> allNodes = new();
        public Dictionary<int, MapNode> nodeLookup = new();

        // Outer Rim: Core Worlds fast travel pairs (node A ↔ node B)
        private static readonly Dictionary<int, int> CoreWorldLinks = new()
        {
            // Naboo (node 6) ↔ Takodama acts as Core World for now
            // In full game, would be Naboo ↔ Coruscant
        };

        public override void OnNetworkSpawn()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
                foreach (var node in FindObjectsOfType<MapNode>())
                {
                    nodeLookup[node.NodeId] = node;
                    allNodes.Add(node);
                }
            }
            else { Destroy(gameObject); }
        }

        public List<int> GetReachableNodes(int fromNodeId, int maxDistance)
        {
            if (!nodeLookup.ContainsKey(fromNodeId)) return new List<int>();
            var result = new List<int>();
            var visited = new HashSet<int>();
            var queue = new Queue<(int nodeId, int distance)>();
            queue.Enqueue((fromNodeId, 0));
            visited.Add(fromNodeId);

            while (queue.Count > 0)
            {
                var (currentId, dist) = queue.Dequeue();
                result.Add(currentId);
                if (dist >= maxDistance) continue;

                if (nodeLookup.TryGetValue(currentId, out var node))
                {
                    foreach (var neighborId in node.ConnectedNodeIds)
                    {
                        if (!visited.Contains(neighborId))
                        { visited.Add(neighborId); queue.Enqueue((neighborId, dist + 1)); }
                    }

                    // Core Worlds fast travel: if at a Core World, can jump to its paired world for 0 movement
                    if (CoreWorldLinks.TryGetValue(currentId, out int pairedId) && !visited.Contains(pairedId))
                    { visited.Add(pairedId); queue.Enqueue((pairedId, dist)); }
                }
            }
            return result;
        }

        public List<int> FindPath(int fromNodeId, int toNodeId)
        {
            if (fromNodeId == toNodeId) return new List<int> { fromNodeId };
            if (!nodeLookup.ContainsKey(fromNodeId) || !nodeLookup.ContainsKey(toNodeId))
                return new List<int>();

            var parent = new Dictionary<int, int>();
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(fromNodeId);
            visited.Add(fromNodeId);

            while (queue.Count > 0)
            {
                int currentId = queue.Dequeue();
                if (!nodeLookup.TryGetValue(currentId, out var currentNode)) continue;

                var neighbors = new List<int>(currentNode.ConnectedNodeIds);
                // Add Core World jump
                if (CoreWorldLinks.TryGetValue(currentId, out int paired))
                    neighbors.Add(paired);

                foreach (var neighborId in neighbors)
                {
                    if (visited.Contains(neighborId)) continue;
                    visited.Add(neighborId);
                    parent[neighborId] = currentId;
                    if (neighborId == toNodeId)
                    {
                        var path = new List<int>();
                        int step = toNodeId;
                        while (step != fromNodeId)
                        { path.Add(step); step = parent[step]; }
                        path.Add(fromNodeId);
                        path.Reverse();
                        return path;
                    }
                    queue.Enqueue(neighborId);
                }
            }
            return new List<int>();
        }

        public int GetPathCost(int fromNodeId, int toNodeId)
        {
            var path = FindPath(fromNodeId, toNodeId);
            return path.Count > 0 ? path.Count - 1 : -1;
        }

        public bool AreConnected(int nodeA, int nodeB)
        {
            if (nodeLookup.TryGetValue(nodeA, out var node) && node.ConnectedNodeIds.Contains(nodeB))
                return true;
            return CoreWorldLinks.TryGetValue(nodeA, out int paired) && paired == nodeB;
        }

        /// <summary>Get all players at a given node (for trade detection).</summary>
        public List<PlayerState> GetPlayersAtNode(int nodeId)
        {
            var result = new List<PlayerState>();
            foreach (var ps in FindObjectsOfType<PlayerState>())
            {
                if (ps.CurrentNodeId.Value == nodeId)
                    result.Add(ps);
            }
            return result;
        }
    }
}
