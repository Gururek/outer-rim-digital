// ShipMovement.cs — Handles ship movement animation along node paths.
// Server-authoritative movement; clients receive animation commands via ClientRpc.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class ShipMovement : NetworkBehaviour
    {
        public static ShipMovement Instance { get; private set; }

        [Header("Animation")]
        [SerializeField] private float shipMoveSpeed = 5f;
        [SerializeField] private float nodePauseTime = 0.05f;

        // ─── Runtime state ──────────────────────────────────────────────────
        private Dictionary<ulong, GameObject> playerShipObjects = new Dictionary<ulong, GameObject>();
        private Dictionary<int, MapNode> nodeLookup; // cached from MapManager

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        public override void OnNetworkSpawn()
        {
            if (MapManager.Instance != null)
                nodeLookup = MapManager.Instance.nodeLookup;
        }

        // ─── Ship registration ──────────────────────────────────────────────

        public void RegisterPlayerShip(ulong clientId, GameObject shipObject)
        {
            playerShipObjects[clientId] = shipObject;
        }

        public void UnregisterPlayerShip(ulong clientId)
        {
            playerShipObjects.Remove(clientId);
        }

        // ═════════════════════════════════════════════════════════════════════
        // SERVER: validate and initiate movement
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Server-side: validates movement request, updates PlayerState, broadcasts path.</summary>
        public bool TryMovePlayer(PlayerState player, int destinationNodeId)
        {
            if (!IsServer) return false;
            if (nodeLookup == null || MapManager.Instance == null) return false;

            int startId = player.CurrentNodeId.Value;
            int speed = player.Speed.Value;

            var path = MapManager.Instance.FindPath(startId, destinationNodeId);
            if (path == null || path.Count == 0) return false;
            if (path.Count > speed) return false; // Path too long for speed

            // Update player location (final node only — intermediates are purely visual)
            player.CurrentNodeId.Value = destinationNodeId;

            // Broadcast the full path to all clients for animation
            AnimateShipMovementClientRpc(player.OwnerClientId, path.ToArray());
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestShipMovementServerRpc(int destinationNodeId, ServerRpcParams rpcParams = default)
        {
            if (!IsServer) return;

            var playerObj = NetworkManager.Singleton.ConnectedClients[rpcParams.Receive.SenderClientId].PlayerObject;
            if (playerObj == null) return;
            if (!playerObj.TryGetComponent(out PlayerState player)) return;

            TryMovePlayer(player, destinationNodeId);
        }

        // ═════════════════════════════════════════════════════════════════════
        // CLIENT: animate along path (all clients)
        // ═════════════════════════════════════════════════════════════════════

        [ClientRpc]
        private void AnimateShipMovementClientRpc(ulong clientId, int[] pathNodeIds)
        {
            if (!playerShipObjects.TryGetValue(clientId, out var ship)) return;
            if (nodeLookup == null) return;

            // Build list of MapNode references from path IDs
            var waypoints = new List<MapNode>();
            foreach (int nodeId in pathNodeIds)
            {
                if (nodeLookup.TryGetValue(nodeId, out var node))
                    waypoints.Add(node);
            }

            if (waypoints.Count > 0)
                StartCoroutine(ShipMoveCoroutine(ship, waypoints));
        }

        private IEnumerator ShipMoveCoroutine(GameObject ship, List<MapNode> waypoints)
        {
            foreach (var waypoint in waypoints)
            {
                Vector3 destination = waypoint.transform.position;

                // Smooth movement toward destination
                while (Vector3.Distance(ship.transform.position, destination) > 0.02f)
                {
                    ship.transform.position = Vector3.MoveTowards(
                        ship.transform.position, destination, shipMoveSpeed * Time.deltaTime);

                    // Rotate toward destination
                    Vector3 dir = (destination - ship.transform.position).normalized;
                    if (dir != Vector3.zero)
                        ship.transform.forward = Vector3.Lerp(
                            ship.transform.forward, dir, Time.deltaTime * 8f);

                    yield return null;
                }

                ship.transform.position = destination;
                yield return new WaitForSeconds(nodePauseTime);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // HIGHLIGHT MOVEMENT MODE (client-side)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called on the active client to highlight reachable nodes during movement planning.
        /// </summary>
        public void ShowReachableNodes(PlayerState player)
        {
            if (nodeLookup == null || MapManager.Instance == null) return;

            int startId = player.CurrentNodeId.Value;
            int speed = player.Speed.Value;

            var reachableIds = MapManager.Instance.GetReachableNodes(startId, speed);

            foreach (int nodeId in reachableIds)
            {
                if (nodeLookup.TryGetValue(nodeId, out var node))
                {
                    // Visual highlighting handled by MapNodeVisuals component — Phase 4
                    Debug.Log($"[ShipMovement] Reachable: {nodeId}");
                }
            }
        }
    }
}
