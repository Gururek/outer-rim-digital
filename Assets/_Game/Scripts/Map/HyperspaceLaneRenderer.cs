// HyperspaceLaneRenderer.cs — Phase 2: draws glowing hyperspace lanes between connected nodes
using UnityEngine;
using System.Collections.Generic;

namespace OuterRim
{
    [RequireComponent(typeof(MapManager))]
    public class HyperspaceLaneRenderer : MonoBehaviour
    {
        [Header("Lane Visuals")]
        [SerializeField] private Material laneMaterial;
        [SerializeField] private float laneWidth = 0.1f;
        [SerializeField] private Color laneColor = new(0.3f, 0.5f, 1f, 0.6f);
        [SerializeField] private float yOffset = 0.1f;

        private MapManager mapManager;
        private List<GameObject> laneObjects = new();
        private Dictionary<(int, int), GameObject> drawnLanes = new();

        private void Awake()
        {
            mapManager = GetComponent<MapManager>();
        }

        private void Start()
        {
            DrawAllLanes();
        }

        [ContextMenu("Draw All Lanes")]
        public void DrawAllLanes()
        {
            ClearLanes();
            if (mapManager == null) mapManager = GetComponent<MapManager>();
            if (mapManager.allNodes.Count == 0)
            {
                mapManager.DiscoverNodes();
                if (mapManager.allNodes.Count == 0) return;
            }

            var drawn = new HashSet<(int, int)>();

            foreach (var node in mapManager.allNodes)
            {
                foreach (var targetId in node.ConnectedNodeIds)
                {
                    var targetNode = mapManager.GetNodeById(targetId);
                    if (targetNode == null) continue;

                    // Avoid drawing duplicate lanes (A→B is same as B→A)
                    var key = node.NodeId < targetId
                        ? (node.NodeId, targetId)
                        : (targetId, node.NodeId);
                    if (drawn.Contains(key)) continue;
                    drawn.Add(key);

                    DrawLane(node.transform.position, targetNode.transform.position);
                }
            }

            Debug.Log($"[HyperspaceLane] Drew {drawn.Count} hyperspace lanes.");
        }

        private void DrawLane(Vector3 from, Vector3 to)
        {
            var go = new GameObject("HyperspaceLane");
            go.transform.SetParent(transform);

            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, from + Vector3.up * yOffset);
            lr.SetPosition(1, to + Vector3.up * yOffset);
            lr.startWidth = laneWidth;
            lr.endWidth = laneWidth;
            lr.startColor = laneColor;
            lr.endColor = laneColor;
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;

            if (laneMaterial != null)
                lr.material = laneMaterial;
            else
            {
                // Create a simple emissive material
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.color = laneColor;
                mat.SetColor("_BaseColor", laneColor);
                lr.material = mat;
            }

            laneObjects.Add(go);
        }

        [ContextMenu("Clear Lanes")]
        public void ClearLanes()
        {
            foreach (var go in laneObjects)
            {
                if (go != null) Destroy(go);
            }
            laneObjects.Clear();
            drawnLanes.Clear();
        }

        private void OnDestroy()
        {
            ClearLanes();
        }

        private void OnDrawGizmosSelected()
        {
            if (mapManager == null) mapManager = GetComponent<MapManager>();
            if (mapManager.allNodes.Count == 0) return;

            Gizmos.color = laneColor;
            var drawn = new HashSet<(int, int)>();
            foreach (var node in mapManager.allNodes)
            {
                foreach (var targetId in node.ConnectedNodeIds)
                {
                    var target = mapManager.GetNodeById(targetId);
                    if (target == null) continue;
                    var key = node.NodeId < targetId
                        ? (node.NodeId, targetId)
                        : (targetId, node.NodeId);
                    if (drawn.Contains(key)) continue;
                    drawn.Add(key);
                    Gizmos.DrawLine(
                        node.transform.position + Vector3.up * yOffset,
                        target.transform.position + Vector3.up * yOffset);
                }
            }
        }
    }
}
