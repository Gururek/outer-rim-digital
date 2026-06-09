// NetworkBootstrapper.cs — Lobby + Relay connection flow
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace OuterRim
{
    public class NetworkBootstrapper : MonoBehaviour
    {
        [SerializeField] private string defaultJoinCode = "";
        [SerializeField] private int maxPlayers = 4;

        private string currentJoinCode;
        private UnityTransport transport;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            transport = GetComponent<UnityTransport>();
            if (transport == null)
                transport = gameObject.AddComponent<UnityTransport>();
        }

        private void Start()
        {
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.NetworkConfig.NetworkTransport == null)
            {
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            }
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        public async void StartHost()
        {
            try
            {
                await InitializeServices();

                var lobbyOptions = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new System.Collections.Generic.Dictionary<string, DataObject>
                    {
                        ["joinCode"] = new DataObject(DataObject.VisibilityOptions.Public, "")
                    }
                };

                var lobby = await LobbyService.Instance.CreateLobbyAsync(
                    "Outer Rim Game", maxPlayers, lobbyOptions);

                var allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
                currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions
                {
                    Data = new System.Collections.Generic.Dictionary<string, DataObject>
                    {
                        ["joinCode"] = new DataObject(DataObject.VisibilityOptions.Public, currentJoinCode)
                    }
                });

                transport.SetHostRelayData(
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData);

                EnsurePlayerPrefab();

                NetworkManager.Singleton.StartHost();
                Debug.Log($"[NetworkBootstrapper] Host started. Join code: {currentJoinCode}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NetworkBootstrapper] StartHost failed: {ex}");
            }
        }

        public void StartSolo()
        {
            // Solo mode: skip Unity Relay/Lobby, start local host immediately.
            // The transport keeps its default localhost binding.
            try
            {
                if (NetworkManager.Singleton == null)
                {
                    Debug.LogError("[NetworkBootstrapper] No NetworkManager found.");
                    return;
                }

                if (NetworkManager.Singleton.NetworkConfig.NetworkTransport == null)
                    NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;

                // Tell GameManager we're solo so it starts with 1 player
                if (GameManager.Instance != null)
                    GameManager.Instance.EnableSoloMode();

                EnsurePlayerPrefab();

                NetworkManager.Singleton.StartHost();
                Debug.Log("[NetworkBootstrapper] Solo host started (local only).");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NetworkBootstrapper] StartSolo failed: {ex}");
            }
        }

        public async void StartClient(string joinCode = null)
        {
            try
            {
                await InitializeServices();

                string code = joinCode ?? defaultJoinCode;
                if (string.IsNullOrEmpty(code))
                {
                    Debug.LogError("[NetworkBootstrapper] No join code provided.");
                    return;
                }

                var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code);

                string relayJoinCode = code;
                if (lobby.Data.TryGetValue("joinCode", out var dataObj))
                    relayJoinCode = dataObj.Value;

                var joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);

                transport.SetClientRelayData(
                    joinAllocation.RelayServer.IpV4,
                    (ushort)joinAllocation.RelayServer.Port,
                    joinAllocation.AllocationIdBytes,
                    joinAllocation.Key,
                    joinAllocation.ConnectionData,
                    joinAllocation.HostConnectionData);

                NetworkManager.Singleton.StartClient();
                Debug.Log($"[NetworkBootstrapper] Client connecting to lobby: {lobby.Id}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NetworkBootstrapper] StartClient failed: {ex}");
            }
        }

        private async Task InitializeServices()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private void EnsurePlayerPrefab()
        {
            if (NetworkManager.Singleton.NetworkConfig.PlayerPrefab != null) return;

            var playerGo = new GameObject("Player");
            playerGo.AddComponent<NetworkObject>();
            playerGo.AddComponent<PlayerState>();

            playerGo.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(playerGo);

            NetworkManager.Singleton.AddNetworkPrefab(playerGo);
            NetworkManager.Singleton.NetworkConfig.PlayerPrefab = playerGo;
            Debug.Log("[NetworkBootstrapper] Player prefab registered for auto-spawn.");
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            Debug.Log($"[NetworkBootstrapper] Client {clientId} connected.");
        }
    }
}
