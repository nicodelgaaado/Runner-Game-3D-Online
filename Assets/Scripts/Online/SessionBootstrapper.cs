using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
        public class SessionBootstrapper : MonoBehaviour
        {
            private const uint NetworkTickRate = 50;
            private enum BootstrapState
            {
            Initializing,
            Ready,
            CreatingSession,
            JoiningSession,
            WaitingForPlayer,
            WaitingForSceneLoad,
            Error
        }

        private string joinCodeInput = string.Empty;
        private string statusMessage = "Initializing online services...";
        private BootstrapState state = BootstrapState.Initializing;
        private bool servicesReady;
        private bool sceneLoadRequested;
        private bool networkStartRequested;
        private bool leavingSession;

        public static SessionBootstrapper Instance { get; private set; }
        public ISession CurrentSession { get; private set; }

        private async void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            ConfigureRuntimeForCurrentPlatform();
            EnsureNetworkManager();
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure += HandleTransportFailure;
            await InitializeServicesAsync();
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.Singleton.OnTransportFailure -= HandleTransportFailure;
            }

            if (CurrentSession != null)
            {
                UnregisterSessionHandlers(CurrentSession);
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public async Task CreatePrivateMatchAsync()
        {
            if (!servicesReady || state == BootstrapState.CreatingSession || state == BootstrapState.JoiningSession)
            {
                return;
            }

            state = BootstrapState.CreatingSession;
            statusMessage = "Creating private online match...";

            try
            {
                SessionOptions options = new SessionOptions
                {
                    MaxPlayers = 2,
                    IsPrivate = true
                };

                CurrentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
                SessionRuntime.SetSession(CurrentSession);
                RegisterSessionHandlers(CurrentSession);

                state = BootstrapState.WaitingForPlayer;
                sceneLoadRequested = false;
                UpdateWaitingStatus();
            }
            catch (Exception exception)
            {
                SetError($"Failed to create session: {exception.Message}");
            }
        }

        public async Task JoinPrivateMatchAsync(string joinCode)
        {
            if (!servicesReady || string.IsNullOrWhiteSpace(joinCode))
            {
                return;
            }

            state = BootstrapState.JoiningSession;
            statusMessage = "Joining private online match...";

            try
            {
                CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim().ToUpperInvariant());
                SessionRuntime.SetSession(CurrentSession);
                RegisterSessionHandlers(CurrentSession);
                sceneLoadRequested = false;
                state = BootstrapState.WaitingForSceneLoad;
                UpdateStatusFromNetworkState(CurrentSession.Network.State);
            }
            catch (Exception exception)
            {
                SetError($"Failed to join session: {exception.Message}");
            }
        }

        public async Task StartMatchWhenFullAsync()
        {
            if (CurrentSession == null || !CurrentSession.IsHost)
            {
                return;
            }

            if (CurrentSession.Players.Count < 2)
            {
                UpdateWaitingStatus();
                return;
            }

            if (CurrentSession.Network.State == NetworkState.Stopped && !networkStartRequested)
            {
                networkStartRequested = true;
                state = BootstrapState.WaitingForSceneLoad;
                statusMessage = "Player 2 joined. Starting Relay connection...";

                try
                {
                    await CurrentSession.AsHost().Network.StartRelayNetworkAsync(RelayNetworkOptions.Default);
                }
                catch (Exception exception)
                {
                    networkStartRequested = false;
                    SetError($"Failed to start network: {exception.Message}");
                }

                return;
            }

            TryStartGameplaySceneOnHost();
        }

        public void LeaveSession()
        {
            if (leavingSession)
            {
                return;
            }

            _ = LeaveSessionInternalAsync();
        }

        private async Task InitializeServicesAsync()
        {
            if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
            {
                SetError("Unity project is not linked to a Unity Dashboard project. In the Editor, open Edit > Project Settings > Services, sign in, choose your organization, then link or create a project ID.");
                return;
            }

            try
            {
                await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                servicesReady = true;
                state = BootstrapState.Ready;
                statusMessage = "Ready. Host a match or join with a code.";
            }
            catch (Exception exception)
            {
                SetError($"Online services failed to initialize: {exception.Message}");
            }
        }

        private void EnsureNetworkManager()
        {
            GameObject playerPrefab = Resources.Load<GameObject>("RunnerNetworkPlayer");
            if (playerPrefab == null)
            {
                throw new InvalidOperationException("Missing Resources/RunnerNetworkPlayer prefab required for online session startup.");
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                GameObject networkManagerObject = new GameObject("NetworkManager");
                DontDestroyOnLoad(networkManagerObject);
                networkManager = networkManagerObject.AddComponent<NetworkManager>();
            }

            UnityTransport transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = networkManager.gameObject.AddComponent<UnityTransport>();
            }

            ConfigureTransportForCurrentPlatform(transport);

            if (networkManager.NetworkConfig == null)
            {
                networkManager.NetworkConfig = new NetworkConfig();
            }

            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.TickRate = NetworkTickRate;
            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.ForceSamePrefabs = true;

            if (!networkManager.NetworkConfig.Prefabs.Contains(playerPrefab))
            {
                networkManager.AddNetworkPrefab(playerPrefab);
            }
        }

        private static void ConfigureRuntimeForCurrentPlatform()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Application.runInBackground = true;
#endif
        }

        private static void ConfigureTransportForCurrentPlatform(UnityTransport transport)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            transport.UseWebSockets = true;
#endif
        }

        private void RegisterSessionHandlers(ISession session)
        {
            session.PlayerJoined += HandlePlayerJoined;
            session.PlayerLeaving += HandlePlayerLeft;
            session.Network.StateChanged += HandleNetworkStateChanged;
            session.Network.StartFailed += HandleNetworkStartFailed;
        }

        private void UnregisterSessionHandlers(ISession session)
        {
            session.PlayerJoined -= HandlePlayerJoined;
            session.PlayerLeaving -= HandlePlayerLeft;
            session.Network.StateChanged -= HandleNetworkStateChanged;
            session.Network.StartFailed -= HandleNetworkStartFailed;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (CurrentSession == null || NetworkManager.Singleton == null || leavingSession)
            {
                return;
            }

            if (NetworkManager.Singleton.IsServer)
            {
                if (clientId != NetworkManager.Singleton.LocalClientId)
                {
                    statusMessage = "Player 2 connected to Relay. Preparing race...";
                }

                networkStartRequested = true;
                TryStartGameplaySceneOnHost();
            }
            else if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                statusMessage = "Connected to host. Waiting for race scene...";
            }
        }

        private async void HandlePlayerJoined(string _)
        {
            if (CurrentSession != null && CurrentSession.IsHost)
            {
                UpdateWaitingStatus();
                await StartMatchWhenFullAsync();
            }
        }

        private void HandlePlayerLeft(string _)
        {
            if (CurrentSession == null)
            {
                return;
            }

            networkStartRequested = false;

            if (CurrentSession.IsHost && !sceneLoadRequested)
            {
                UpdateWaitingStatus();
                return;
            }

            SetError("The other player left the match.");
            LeaveSession();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (CurrentSession == null || networkManager == null || leavingSession)
            {
                return;
            }

            bool isLocalDisconnect = clientId == networkManager.LocalClientId;
            bool isHost = networkManager.IsServer;

            if (isHost && !isLocalDisconnect)
            {
                sceneLoadRequested = false;
                networkStartRequested = false;

                if (SceneManager.GetActiveScene().name == "Bootstrap")
                {
                    state = BootstrapState.WaitingForPlayer;
                    UpdateWaitingStatus();
                    return;
                }
            }

            SetError("Disconnected from the online session.");
            LeaveSession();
        }

        private void HandleTransportFailure()
        {
            if (leavingSession)
            {
                return;
            }

            SetError("Network transport failed. Returning to menu.");
            LeaveSession();
        }

        private void HandleNetworkStateChanged(NetworkState networkState)
        {
            if (CurrentSession == null || leavingSession)
            {
                return;
            }

            UpdateStatusFromNetworkState(networkState);

            if (CurrentSession.IsHost && networkState == NetworkState.Started)
            {
                TryStartGameplaySceneOnHost();
            }
        }

        private void HandleNetworkStartFailed(SessionError error)
        {
            networkStartRequested = false;
            SetError($"Failed to start network: {error}");
        }

        private async Task LeaveSessionInternalAsync()
        {
            leavingSession = true;

            try
            {
                if (CurrentSession != null)
                {
                    UnregisterSessionHandlers(CurrentSession);
                    await CurrentSession.LeaveAsync();
                }
            }
            catch (Exception exception)
            {
                if (!IsExpectedLeaveException(exception))
                {
                    Debug.LogWarning($"Leaving session failed: {exception.Message}");
                }
            }
            finally
            {
                CurrentSession = null;
                sceneLoadRequested = false;
                networkStartRequested = false;
                SessionRuntime.Clear();
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                state = BootstrapState.Ready;
                statusMessage = "Returned to menu.";
                if (SceneManager.GetActiveScene().name != "Bootstrap")
                {
                    SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
                }

                leavingSession = false;
            }
        }

        private void SetError(string message)
        {
            state = BootstrapState.Error;
            statusMessage = message;
        }

        private void UpdateWaitingStatus()
        {
            if (CurrentSession == null)
            {
                return;
            }

            state = BootstrapState.WaitingForPlayer;
            statusMessage = $"Waiting for player 2... ({CurrentSession.Players.Count}/2)";
        }

        private void TryStartGameplaySceneOnHost()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (sceneLoadRequested || CurrentSession == null || networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            if (SceneManager.GetActiveScene().name != "Bootstrap")
            {
                return;
            }

            if (CurrentSession.Players.Count < 2)
            {
                UpdateWaitingStatus();
                return;
            }

            if (CurrentSession.Network.State != NetworkState.Started || !networkManager.IsListening)
            {
                state = BootstrapState.WaitingForSceneLoad;
                statusMessage = "Player 2 joined. Starting network...";
                return;
            }

            if (networkManager.ConnectedClientsIds.Count < 2)
            {
                state = BootstrapState.WaitingForSceneLoad;
                statusMessage = "Player 2 joined session. Waiting for network connection...";
                return;
            }

            sceneLoadRequested = true;
            state = BootstrapState.WaitingForSceneLoad;
            statusMessage = "Both players connected. Loading race scene...";
            networkManager.SceneManager.LoadScene("Joc", LoadSceneMode.Single);
        }

        private void UpdateStatusFromNetworkState(NetworkState networkState)
        {
            if (CurrentSession == null)
            {
                return;
            }

            switch (networkState)
            {
                case NetworkState.Stopped:
                    if (CurrentSession.IsHost)
                    {
                        UpdateWaitingStatus();
                    }
                    else
                    {
                        state = BootstrapState.WaitingForPlayer;
                        statusMessage = "Joined session. Waiting for host to start the race...";
                    }
                    break;
                case NetworkState.Starting:
                    state = BootstrapState.WaitingForSceneLoad;
                    statusMessage = CurrentSession.IsHost
                        ? "Starting Relay connection..."
                        : "Connecting to host...";
                    break;
                case NetworkState.Started:
                    state = BootstrapState.WaitingForSceneLoad;
                    statusMessage = CurrentSession.IsHost
                        ? "Relay ready. Waiting for player connection..."
                        : "Connected. Waiting for host to load the race...";
                    break;
                case NetworkState.Stopping:
                    state = BootstrapState.WaitingForSceneLoad;
                    statusMessage = "Closing online session...";
                    break;
                case NetworkState.Migrating:
                    state = BootstrapState.WaitingForSceneLoad;
                    statusMessage = "Network migration in progress...";
                    break;
            }
        }

        private static bool IsExpectedLeaveException(Exception exception)
        {
            string message = exception.Message.ToLowerInvariant();
            return message.Contains("lobby not found")
                || message.Contains("session was never started")
                || message.Contains("trying to stop the network when it is not started");
        }

        private static bool ShouldShowBootstrapGui()
        {
            Scene gameplayScene = SceneManager.GetSceneByName("Joc");
            if (gameplayScene.IsValid() && gameplayScene.isLoaded)
            {
                return false;
            }

            return SceneManager.GetActiveScene().name == "Bootstrap";
        }

        private void OnGUI()
        {
            if (!ShouldShowBootstrapGui())
            {
                return;
            }

            Rect area = new Rect((Screen.width * 0.5f) - 220f, (Screen.height * 0.5f) - 180f, 440f, 360f);
            GUILayout.BeginArea(area, GUI.skin.window);
            GUILayout.Label("Runner Game Online");
            GUILayout.Space(8f);
            GUILayout.Label(statusMessage);
            GUILayout.Space(12f);

            GUI.enabled = state == BootstrapState.Ready || state == BootstrapState.Error;
            if (GUILayout.Button("Host Private Match"))
            {
                _ = CreatePrivateMatchAsync();
            }

            GUILayout.Space(8f);
            GUILayout.Label("Join Code");
            joinCodeInput = GUILayout.TextField(joinCodeInput, 16);
            if (GUILayout.Button("Join Match"))
            {
                _ = JoinPrivateMatchAsync(joinCodeInput);
            }

            GUI.enabled = true;

            if (CurrentSession != null)
            {
                GUILayout.Space(12f);
                GUILayout.Label($"Session Code: {CurrentSession.Code}");
                GUILayout.Label($"Players: {CurrentSession.Players.Count}/2");
            }

            GUILayout.EndArea();
        }
    }
}
