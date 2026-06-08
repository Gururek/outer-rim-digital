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

        // Core Worlds fast travel pairs
        private static readonly Dictionary<int, int> CoreWorldLinks = new()
        {
            // Naboo (6) ↔ Takodama (8) as placeholder Core Worlds
        };

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else { Destroy(gameObject); return; }
            DiscoverNodes();
        }

        public override void OnNetworkSpawn()
        {
            if (_instance == null) _instance = this;
            DiscoverNodes();
        }

        /// <summary>Find all MapNode components in the scene. Callable from editor.</summary>
        public void DiscoverNodes()
        {
            nodeLookup.Clear();
            allNodes.Clear();
            foreach (var node in FindObjectsOfType<MapNode>())
            {
                if (!nodeLookup.ContainsKey(node.NodeId))
                {
                    nodeLookup[node.NodeId] = node;
                    allNodes.Add(node);
                }
            }
            if (allNodes.Count > 0)
                Debug.Log($"[MapManager] Discovered {allNodes.Count} nodes.");
        }

        public MapNode GetNodeById(int id) => nodeLookup.TryGetValue(id, out var n) ? n : null;

        public List<int> GetReachableNodes(int fromNodeId, int maxDistance)
        {
            if (!nodeLookup.ContainsKey(fromNodeId)) return new List<int>();
            var result = new List<int>();
            var visited = new HashSet<int>();
            var queue = new Queue<(int, int)>();
            queue.Enqueue((fromNodeId, 0));
            visited.Add(fromNodeId);

            while (queue.Count > 0)
            {
                var (cur, dist) = queue.Dequeue();
                result.Add(cur);
                if (dist >= maxDistance) continue;

                if (nodeLookup.TryGetValue(cur, out var node))
                {
                    foreach (var nid in node.ConnectedNodeIds)
                        if (!visited.Contains(nid)) { visited.Add(nid); queue.Enqueue((nid, dist + 1)); }

                    if (CoreWorldLinks.TryGetValue(cur, out int paired) && !visited.Contains(paired))
                    { visited.Add(paired); queue.Enqueue((paired, dist)); }
                }
            }
            return result;
        }

        public List<int> FindPath(int from, int to)
        {
            if (from == to) return new List<int> { from };
            if (!nodeLookup.ContainsKey(from) || !nodeLookup.ContainsKey(to)) return new List<int>();

            var parent = new Dictionary<int, int>();
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(from);
            visited.Add(from);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (!nodeLookup.TryGetValue(cur, out var cn)) continue;

                var neighbors = new List<int>(cn.ConnectedNodeIds);
                if (CoreWorldLinks.TryGetValue(cur, out int p)) neighbors.Add(p);

                foreach (var nid in neighbors)
                {
                    if (visited.Contains(nid)) continue;
                    visited.Add(nid);
                    parent[nid] = cur;
                    if (nid == to)
                    {
                        var path = new List<int>();
                        int s = to;
                        while (s != from) { path.Add(s); s = parent[s]; }
                        path.Add(from); path.Reverse();
                        return path;
                    }
                    queue.Enqueue(nid);
                }
            }
            return new List<int>();
        }

        public int GetPathCost(int from, int to) { var p = FindPath(from, to); return p.Count > 0 ? p.Count - 1 : -1; }

        public List<PlayerState> GetPlayersAtNode(int nodeId)
        {
            var r = new List<PlayerState>();
            foreach (var ps in FindObjectsOfType<PlayerState>())
                if (ps.CurrentNodeId.Value == nodeId) r.Add(ps);
            return r;
        }
    }
}
