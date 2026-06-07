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
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        /// <summary>Initialize Unity Services, sign in, create lobby, allocate Relay, start host.</summary>
        public async void StartHost()
        {
            try
            {
                await InitializeServices();

                // Create lobby
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

                // Allocate Relay
                var allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
                currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                // Update lobby with join code
                await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions
                {
                    Data = new System.Collections.Generic.Dictionary<string, DataObject>
                    {
                        ["joinCode"] = new DataObject(DataObject.VisibilityOptions.Public, currentJoinCode)
                    }
                });

                // Set Relay transport data
                transport.SetHostRelayData(
                    allocation.RelayServer.IpV4,
                    allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData);

                // Start host
                NetworkManager.Singleton.StartHost();
                Debug.Log($"[NetworkBootstrapper] Host started. Join code: {currentJoinCode}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NetworkBootstrapper] StartHost failed: {ex}");
            }
        }

        /// <summary>Initialize Unity Services, join lobby by code, join Relay, start client.</summary>
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

                // Join lobby by code
                var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code);

                // Retrieve Relay join code from lobby data
                string relayJoinCode = code;
                if (lobby.Data.TryGetValue("joinCode", out var dataObj))
                    relayJoinCode = dataObj.Value;

                // Join Relay
                var joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);

                // Set Relay transport data
                transport.SetClientRelayData(
                    joinAllocation.RelayServer.IpV4,
                    joinAllocation.RelayServer.Port,
                    joinAllocation.AllocationIdBytes,
                    joinAllocation.Key,
                    joinAllocation.ConnectionData,
                    joinAllocation.HostConnectionData);

                // Start client
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

        /// <summary>Server spawns player objects when clients connect.</summary>
        private void OnClientConnected(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            // Spawn network manager child objects — GameManager, MapManager etc
            // Player spawning is handled by NetworkManager's PlayerPrefab
            Debug.Log($"[NetworkBootstrapper] Client {clientId} connected.");
        }
    }
}
