using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public class SessionBootstrapper : MonoBehaviour, INetworkRunnerCallbacks
    {
        private const int MaxPlayers = 2;
        private const string GameplaySceneName = "Joc";
        private const string BootstrapSceneName = "Bootstrap";
        private const string PlayerPrefabResourcePath = "RunnerNetworkPlayer";
        private const string RaceManagerPrefabResourcePath = "NetworkRaceManager";
        private const string RunnerObjectName = "FusionRunner";
        private const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private enum BootstrapState
        {
            Ready,
            CreatingSession,
            JoiningSession,
            WaitingForPlayer,
            WaitingForSceneLoad,
            Error
        }

        private readonly HashSet<PlayerRef> activePlayers = new();

        private string joinCodeInput = string.Empty;
        private string statusMessage = "Ready. Host a match or join with a code.";
        private BootstrapState state = BootstrapState.Ready;
        private bool sceneLoadRequested;
        private bool leavingSession;
        private bool handlingShutdown;
        private NetworkRunner runner;

        public static SessionBootstrapper Instance { get; private set; }

        public NetworkRunner Runner => runner;
        public string CurrentSessionCode => SessionRuntime.SessionCode;
        public int CurrentPlayerCount => activePlayers.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            ConfigureRuntimeForCurrentPlatform();
        }

        private void OnDestroy()
        {
            if (runner != null)
            {
                runner.RemoveCallbacks(this);
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public async Task CreatePrivateMatchAsync()
        {
            if (!CanStartNewSession())
            {
                return;
            }

            string roomCode = GenerateRoomCode();
            state = BootstrapState.CreatingSession;
            statusMessage = $"Creating private room {roomCode}...";
            await StartSessionAsync(roomCode);
        }

        public async Task JoinPrivateMatchAsync(string joinCode)
        {
            if (!CanStartNewSession() || string.IsNullOrWhiteSpace(joinCode))
            {
                return;
            }

            string normalizedCode = joinCode.Trim().ToUpperInvariant();
            state = BootstrapState.JoiningSession;
            statusMessage = $"Joining private room {normalizedCode}...";
            await StartSessionAsync(normalizedCode);
        }

        public void LeaveSession()
        {
            if (leavingSession)
            {
                return;
            }

            _ = LeaveSessionInternalAsync(loadBootstrapScene: true, "Returned to menu.");
        }

        private bool CanStartNewSession()
        {
            return !leavingSession && (state == BootstrapState.Ready || state == BootstrapState.Error);
        }

        private async Task StartSessionAsync(string sessionCode)
        {
            sceneLoadRequested = false;
            activePlayers.Clear();

            try
            {
                runner = CreateRunner();
                SessionRuntime.SetSession(runner, sessionCode);

                var startScene = new NetworkSceneInfo();
                startScene.AddSceneRef(SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{BootstrapSceneName}.unity")), LoadSceneMode.Single);

                StartGameResult startResult = await runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    SessionName = sessionCode,
                    PlayerCount = MaxPlayers,
                    Scene = startScene,
                    SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                    ObjectProvider = runner.GetComponent<NetworkObjectProviderDefault>(),
                });

                if (!startResult.Ok)
                {
                    throw new InvalidOperationException($"Fusion StartGame failed: {startResult.ShutdownReason}");
                }

                statusMessage = "Connected. Waiting for player 2...";
                state = BootstrapState.WaitingForPlayer;
            }
            catch (Exception exception)
            {
                SetError($"Failed to start Fusion session: {exception.Message}");
                await CleanupRunnerAsync(loadBootstrapScene: false);
            }
        }

        private NetworkRunner CreateRunner()
        {
            GameObject runnerObject = new GameObject(RunnerObjectName);
            DontDestroyOnLoad(runnerObject);

            NetworkRunner createdRunner = runnerObject.AddComponent<NetworkRunner>();
            createdRunner.ProvideInput = true;

            runnerObject.AddComponent<NetworkSceneManagerDefault>();
            runnerObject.AddComponent<NetworkObjectProviderDefault>();

            createdRunner.AddCallbacks(this);
            return createdRunner;
        }

        private async Task LeaveSessionInternalAsync(bool loadBootstrapScene, string readyMessage)
        {
            leavingSession = true;
            statusMessage = "Leaving session...";

            try
            {
                await CleanupRunnerAsync(loadBootstrapScene);
            }
            finally
            {
                state = BootstrapState.Ready;
                statusMessage = readyMessage;
                leavingSession = false;
                handlingShutdown = false;
                sceneLoadRequested = false;
                activePlayers.Clear();
            }
        }

        private async Task CleanupRunnerAsync(bool loadBootstrapScene)
        {
            NetworkRunner currentRunner = runner;
            if (currentRunner != null)
            {
                currentRunner.RemoveCallbacks(this);
                runner = null;

                if (currentRunner.IsRunning)
                {
                    await currentRunner.Shutdown(false, ShutdownReason.Ok, true);
                }

                if (currentRunner != null)
                {
                    Destroy(currentRunner.gameObject);
                }
            }

            SessionRuntime.Clear();

            if (loadBootstrapScene && SceneManager.GetActiveScene().name != BootstrapSceneName)
            {
                SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            }
        }

        private void SetError(string message)
        {
            state = BootstrapState.Error;
            statusMessage = message;
        }

        private static void ConfigureRuntimeForCurrentPlatform()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Application.runInBackground = true;
#endif
        }

        private static string GenerateRoomCode()
        {
            char[] buffer = new char[6];
            byte[] randomBytes = new byte[buffer.Length];
            RandomNumberGenerator.Fill(randomBytes);
            for (int index = 0; index < buffer.Length; index++)
            {
                buffer[index] = RoomCodeAlphabet[randomBytes[index] % RoomCodeAlphabet.Length];
            }

            return new string(buffer);
        }

        private bool ShouldShowBootstrapGui()
        {
            Scene gameplayScene = SceneManager.GetSceneByName(GameplaySceneName);
            if (gameplayScene.IsValid() && gameplayScene.isLoaded)
            {
                return false;
            }

            return SceneManager.GetActiveScene().name == BootstrapSceneName;
        }

        private void TryLoadGameplayScene()
        {
            if (runner == null || !runner.IsRunning || !runner.IsSceneAuthority || sceneLoadRequested)
            {
                return;
            }

            if (activePlayers.Count < MaxPlayers || SceneManager.GetActiveScene().name != BootstrapSceneName)
            {
                state = BootstrapState.WaitingForPlayer;
                statusMessage = $"Waiting for player 2... ({activePlayers.Count}/{MaxPlayers})";
                return;
            }

            sceneLoadRequested = true;
            state = BootstrapState.WaitingForSceneLoad;
            statusMessage = "Both players connected. Loading race scene...";
            runner.LoadScene(SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{GameplaySceneName}.unity")), LoadSceneMode.Single);
        }

        private void EnsureGameplaySessionObjects(NetworkRunner networkRunner)
        {
            if (networkRunner.IsSceneAuthority)
            {
                NetworkObject raceManagerPrefab = Resources.Load<NetworkObject>(RaceManagerPrefabResourcePath);
                if (raceManagerPrefab == null)
                {
                    throw new InvalidOperationException($"Missing Resources/{RaceManagerPrefabResourcePath}.prefab required for Fusion gameplay startup.");
                }

                if (NetworkRaceManager.Instance == null)
                {
                    networkRunner.Spawn(raceManagerPrefab, flags: NetworkSpawnFlags.SharedModeStateAuthMasterClient);
                }
            }

            if (!networkRunner.IsPlayerValid(networkRunner.LocalPlayer))
            {
                return;
            }

            NetworkObject playerPrefab = Resources.Load<NetworkObject>(PlayerPrefabResourcePath);
            if (playerPrefab == null)
            {
                throw new InvalidOperationException($"Missing Resources/{PlayerPrefabResourcePath}.prefab required for Fusion gameplay startup.");
            }

            if (networkRunner.TryGetPlayerObject(networkRunner.LocalPlayer, out NetworkObject existingObject) && existingObject != null)
            {
                return;
            }

            NetworkObject playerObject = networkRunner.Spawn(
                playerPrefab,
                position: Vector3.zero,
                rotation: Quaternion.identity,
                inputAuthority: networkRunner.LocalPlayer,
                flags: NetworkSpawnFlags.SharedModeStateAuthLocalPlayer);

            networkRunner.SetPlayerObject(networkRunner.LocalPlayer, playerObject);
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

            if (SessionRuntime.HasSession)
            {
                GUILayout.Space(12f);
                GUILayout.Label($"Room Code: {SessionRuntime.SessionCode}");
                GUILayout.Label($"Players: {activePlayers.Count}/{MaxPlayers}");
                GUILayout.Label($"Mode: Shared");
            }

            GUILayout.EndArea();
        }

        public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef player)
        {
            activePlayers.Add(player);

            if (networkRunner.IsSceneAuthority)
            {
                statusMessage = $"Player joined. ({activePlayers.Count}/{MaxPlayers})";
                TryLoadGameplayScene();
            }
            else if (player == networkRunner.LocalPlayer)
            {
                statusMessage = "Connected. Waiting for host to start the race...";
            }
        }

        public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
        {
            activePlayers.Remove(player);

            if (SceneManager.GetActiveScene().name == GameplaySceneName && networkRunner.IsSceneAuthority)
            {
                sceneLoadRequested = false;
                statusMessage = "The other player left the match. Returning to menu...";
                networkRunner.LoadScene(SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{BootstrapSceneName}.unity")), LoadSceneMode.Single);
                return;
            }

            statusMessage = activePlayers.Count < MaxPlayers
                ? $"Waiting for player 2... ({activePlayers.Count}/{MaxPlayers})"
                : statusMessage;
        }

        public void OnInput(NetworkRunner networkRunner, NetworkInput input)
        {
            input.Set(RunnerInputAdapter.CaptureCurrentDevices());
        }

        public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason)
        {
            SessionRuntime.SetShutdownReason(shutdownReason);
            if (handlingShutdown)
            {
                return;
            }

            handlingShutdown = true;
            _ = LeaveSessionInternalAsync(loadBootstrapScene: true, $"Session closed: {shutdownReason}");
        }

        public void OnSceneLoadDone(NetworkRunner networkRunner)
        {
            string activeSceneName = SceneManager.GetActiveScene().name;
            if (activeSceneName == GameplaySceneName)
            {
                try
                {
                    EnsureGameplaySessionObjects(networkRunner);
                    statusMessage = "Race scene ready.";
                }
                catch (Exception exception)
                {
                    SetError($"Failed to spawn Fusion session objects: {exception.Message}");
                }
            }
            else if (activeSceneName == BootstrapSceneName)
            {
                sceneLoadRequested = false;
                statusMessage = SessionRuntime.HasSession
                    ? $"Waiting for player 2... ({activePlayers.Count}/{MaxPlayers})"
                    : "Ready. Host a match or join with a code.";
                state = SessionRuntime.HasSession ? BootstrapState.WaitingForPlayer : BootstrapState.Ready;
            }
        }

        public void OnConnectedToServer(NetworkRunner networkRunner) { }
        public void OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason)
        {
            SetError($"Disconnected from Photon: {reason}");
        }
        public void OnConnectRequest(NetworkRunner networkRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner networkRunner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            SetError($"Failed to connect: {reason}");
        }
        public void OnUserSimulationMessage(NetworkRunner networkRunner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner networkRunner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner networkRunner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner networkRunner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnInputMissing(NetworkRunner networkRunner, PlayerRef player, NetworkInput input) { }
        public void OnSceneLoadStart(NetworkRunner networkRunner) { }
        public void OnObjectEnterAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectExitAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
    }
}
