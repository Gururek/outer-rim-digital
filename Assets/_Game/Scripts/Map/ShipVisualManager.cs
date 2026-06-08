// ShipVisualManager.cs — Phase 2: spawns and positions 3D ship models for players and patrols
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace OuterRim
{
    public class ShipVisualManager : NetworkBehaviour
    {
        public static ShipVisualManager Instance { get; private set; }

        [Header("Ship Models")]
        [SerializeField] private List<GameObject> shipModelPrefabs = new();

        [Header("Patrol Ship")]
        [SerializeField] private GameObject patrolShipPrefab;

        [Header("Visual Settings")]
        [SerializeField] private float shipYOffset = 0.5f;
        [SerializeField] private float bobHeight = 0.15f;
        [SerializeField] private float bobSpeed = 1.5f;
        [SerializeField] private Color playerColor = new(0f, 0.8f, 1f);
        [SerializeField] private Color patrolColor = new(1f, 0.3f, 0.3f);

        private Dictionary<ulong, GameObject> playerShips = new();
        private Dictionary<int, GameObject> patrolVisuals = new();
        private GameObject defaultShipMesh;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            // Create a fallback ship if no prefabs are assigned
            if (shipModelPrefabs.Count == 0)
            {
                defaultShipMesh = CreateFallbackShip();
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            SpawnPlayerShip(clientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            RemovePlayerShip(clientId);
        }

        public void SpawnPlayerShip(ulong clientId)
        {
            if (playerShips.ContainsKey(clientId)) return;

            var playerObj = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
            if (playerObj == null) return;

            var ps = playerObj.GetComponent<PlayerState>();
            if (ps == null) return;

            var shipGo = GetShipModel(clientId);
            shipGo.transform.SetParent(transform);
            shipGo.name = $"Ship_{clientId}";

            // Color the ship
            var renderers = shipGo.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                foreach (var mat in r.materials)
                    mat.color = playerColor;
            }

            playerShips[clientId] = shipGo;
            PositionShip(clientId, ps.CurrentNodeId.Value);
        }

        public void RemovePlayerShip(ulong clientId)
        {
            if (playerShips.TryGetValue(clientId, out var go))
            {
                if (go != null) Destroy(go);
                playerShips.Remove(clientId);
            }
        }

        public void PositionShip(ulong clientId, int nodeId)
        {
            if (!playerShips.TryGetValue(clientId, out var ship) || ship == null) return;
            var node = MapManager.Instance?.GetNodeById(nodeId);
            if (node == null) return;

            ship.transform.position = node.transform.position + Vector3.up * shipYOffset;
            ship.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
        }

        public void SpawnPatrolVisual(int patrolId, int nodeId, FactionType faction)
        {
            if (patrolVisuals.ContainsKey(patrolId)) return;

            var node = MapManager.Instance?.GetNodeById(nodeId);
            if (node == null) return;

            var go = patrolShipPrefab != null
                ? Instantiate(patrolShipPrefab, transform)
                : CreateFallbackShip();

            go.name = $"Patrol_{patrolId}_{faction}";
            go.transform.position = node.transform.position + Vector3.up * shipYOffset;

            var renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
                foreach (var mat in r.materials)
                    mat.color = GetFactionColor(faction);

            patrolVisuals[patrolId] = go;
        }

        public void MovePatrolVisual(int patrolId, int newNodeId)
        {
            if (!patrolVisuals.TryGetValue(patrolId, out var go) || go == null) return;
            var node = MapManager.Instance?.GetNodeById(newNodeId);
            if (node == null) return;

            StartCoroutine(MoveShipCoroutine(go, node.transform.position + Vector3.up * shipYOffset));
        }

        private System.Collections.IEnumerator MoveShipCoroutine(GameObject ship, Vector3 target)
        {
            Vector3 start = ship.transform.position;
            float duration = 1.5f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                ship.transform.position = Vector3.Lerp(start, target, t);
                yield return null;
            }
            ship.transform.position = target;
        }

        private void Update()
        {
            // Gentle bobbing animation
            float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            foreach (var kvp in playerShips)
            {
                if (kvp.Value == null) continue;
                var pos = kvp.Value.transform.position;
                kvp.Value.transform.position = new Vector3(pos.x, pos.y + bob * 0.3f, pos.z);
            }
        }

        private GameObject GetShipModel(ulong clientId)
        {
            if (shipModelPrefabs.Count > 0)
            {
                int index = (int)(clientId % (ulong)shipModelPrefabs.Count);
                return Instantiate(shipModelPrefabs[index]);
            }
            return Instantiate(defaultShipMesh);
        }

        private GameObject CreateFallbackShip()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = new Vector3(0.4f, 0.15f, 0.7f);
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        private Color GetFactionColor(FactionType faction) => faction switch
        {
            FactionType.Hutt      => new Color(1f, 0.7f, 0f),
            FactionType.Syndicate => new Color(1f, 0.3f, 0f),
            FactionType.Imperial  => new Color(0.3f, 0.3f, 1f),
            FactionType.Rebel     => new Color(1f, 0.2f, 0.2f),
            _ => Color.gray
        };

        public override void OnNetworkDespawn()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            foreach (var go in playerShips.Values)
                if (go != null) Destroy(go);
            foreach (var go in patrolVisuals.Values)
                if (go != null) Destroy(go);
            playerShips.Clear();
            patrolVisuals.Clear();
        }
    }
}
