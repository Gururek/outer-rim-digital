// NodeClickHandler.cs — OnMouseDown handler for 3D map node clicking.
// Attached to each MapNode GameObject by MapBuilder.
using UnityEngine;
using Unity.Netcode;

namespace OuterRim
{
    public class NodeClickHandler : MonoBehaviour
    {
        [SerializeField] private int nodeId = -1;
        public int NodeId { get => nodeId; set => nodeId = value; }

        private void OnMouseDown()
        {
            if (nodeId < 0) return;

            var net = NetworkManager.Singleton;
            if (net == null || !net.IsConnectedClient) return;

            var gm = GameManager.Instance;
            if (gm == null) return;

            if (gm.CurrentPhase != GamePhase.ActionPhase) return;

            var ap = gm.GetActivePlayer();
            if (ap == null || ap.OwnerClientId != net.LocalClientId) return;

            // Validate reachability
            if (MapManager.Instance != null)
            {
                var reachable = MapManager.Instance.GetReachableNodes(ap.CurrentNodeId.Value, ap.Hyperdrive.Value);
                if (!reachable.Contains(nodeId))
                {
                    Debug.LogWarning($"[NodeClickHandler] Node {nodeId} not reachable from {ap.CurrentNodeId.Value}");
                    return;
                }
            }

            Debug.Log($"[NodeClickHandler] Clicking node {nodeId} to move.");
            gm.ConfirmMoveServerRpc(nodeId);
        }

        private void OnMouseEnter()
        {
            var renderer = GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.white;
            }
        }

        private void OnMouseExit()
        {
            var renderer = GetComponentInChildren<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                // Reset to faction color — MapNode component should store it
                // For now, just dim slightly
                renderer.material.color = Color.gray;
            }
        }
    }
}
