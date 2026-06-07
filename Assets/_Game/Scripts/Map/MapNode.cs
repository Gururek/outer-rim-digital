using UnityEngine;
using System.Collections.Generic;

namespace OuterRim
{
    public class MapNode : MonoBehaviour
    {
        public int NodeId;
        public MapNodeType Type;
        public string NodeName;
        public List<int> ConnectedNodeIds = new List<int>();
        public int? PlanetDeckId;
        public FactionType PlanetFactionType;

        public bool IsOccupied
        {
            get
            {
                foreach (var playerState in FindObjectsOfType<PlayerState>())
                {
                    if (playerState.CurrentNodeId.Value == NodeId)
                        return true;
                }
                return false;
            }
        }

        public bool IsPlanet => Type == MapNodeType.Planet;

        public List<int> GetReachableNodes(int maxDistance)
        {
            return MapManager.Instance.GetReachableNodes(NodeId, maxDistance);
        }
    }
}