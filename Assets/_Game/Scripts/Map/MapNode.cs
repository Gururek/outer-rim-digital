// MapNode.cs — V2
using UnityEngine;
using System.Collections.Generic;

namespace OuterRim
{
    public class MapNode : MonoBehaviour
    {
        public int NodeId;
        public MapNodeType Type;
        public string NodeName;
        public List<int> ConnectedNodeIds = new();
        public FactionType PlanetFactionType;
        public int? PlanetDeckId;

        public bool IsPlanet => Type == MapNodeType.Planet;
        public bool IsMaelstrom => Type == MapNodeType.Maelstrom;

        public List<int> GetReachableNodes(int maxDistance) =>
            MapManager.Instance.GetReachableNodes(NodeId, maxDistance);
    }
}
