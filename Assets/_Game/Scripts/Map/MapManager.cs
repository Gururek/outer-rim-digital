using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace OuterRim
{
    public class MapManager : NetworkBehaviour
    {
        private static MapManager _instance;
        public static MapManager Instance => _instance;

        public List<MapNode> allNodes = new List<MapNode>();
        public Dictionary<int, MapNode> nodeLookup = new Dictionary<int, MapNode>();

        public override void OnNetworkSpawn()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);

                // Populate node lookup from scene
                foreach (var node in FindObjectsOfType<MapNode>())
                {
                    nodeLookup[node.NodeId] = node;
                    allNodes.Add(node);
                }
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>Level-order BFS: returns all nodes within maxDistance hops (inclusive).</summary>
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
                        {
                            visited.Add(neighborId);
                            queue.Enqueue((neighborId, dist + 1));
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>BFS shortest path from fromNodeId to toNodeId. Returns empty list if unreachable.</summary>
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

                foreach (var neighborId in currentNode.ConnectedNodeIds)
                {
                    if (visited.Contains(neighborId)) continue;
                    visited.Add(neighborId);
                    parent[neighborId] = currentId;

                    if (neighborId == toNodeId)
                    {
                        // Reconstruct path
                        var path = new List<int>();
                        int step = toNodeId;
                        while (step != fromNodeId)
                        {
                            path.Add(step);
                            step = parent[step];
                        }
                        path.Add(fromNodeId);
                        path.Reverse();
                        return path;
                    }

                    queue.Enqueue(neighborId);
                }
            }

            return new List<int>(); // unreachable
        }

        /// <summary>Number of edges in shortest path. Returns -1 if unreachable.</summary>
        public int GetPathCost(int fromNodeId, int toNodeId)
        {
            var path = FindPath(fromNodeId, toNodeId);
            return path.Count > 0 ? path.Count - 1 : -1;
        }

        public bool AreConnected(int nodeA, int nodeB)
        {
            return nodeLookup.TryGetValue(nodeA, out var node) &&
                   node.ConnectedNodeIds.Contains(nodeB);
        }
    }
}