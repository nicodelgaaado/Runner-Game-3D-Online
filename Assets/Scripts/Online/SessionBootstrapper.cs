using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public class SessionBootstrapper : MonoBehaviour, INetworkRunnerCallbacks
    {
        private const int MaxPlayers = 2;
        private const string GameplaySceneName = "Joc";
        private const string BootstrapSceneName = "Bootstrap";
        private const string GameplayScenePath = "Assets/Scenes/Joc.unity";
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string PlayerPrefabResourcePath = "RunnerNetworkPlayer";
        private const string RaceManagerPrefabResourcePath = "NetworkRaceManager";
        private const string RunnerObjectName = "FusionRunner";
        private const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int MaxStartAttempts = 3;
        private const int StartRetryDelayMilliseconds = 1500;

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
        private string lastTransportFailureMessage = string.Empty;
        private Material bootstrapSkyboxMaterial;
        private AmbientMode bootstrapAmbientMode;
        private Color bootstrapAmbientSkyColor;
        private Color bootstrapAmbientEquatorColor;
        private Color bootstrapAmbientGroundColor;
        private float bootstrapAmbientIntensity;
        private bool bootstrapFogEnabled;
        private Color bootstrapFogColor;
        private FogMode bootstrapFogMode;
        private float bootstrapFogDensity;
        private float bootstrapLinearFogStart;
        private float bootstrapLinearFogEnd;
        private Color bootstrapSubtractiveShadowColor;

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
            CaptureBootstrapRenderSettings();
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

        private bool IsStartingSession()
        {
            return state == BootstrapState.CreatingSession || state == BootstrapState.JoiningSession;
        }

        private void ResetFailureDetails()
        {
            lastTransportFailureMessage = string.Empty;
        }

        private void RecordTransportFailure(string message, NetworkRunner networkRunner)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lastTransportFailureMessage = message;
            LogSessionSnapshot(message, networkRunner);
        }

        private static string GetBaseExceptionMessage(Exception exception)
        {
            return exception?.GetBaseException().Message ?? string.Empty;
        }

        private bool IsTransientCloudConnectFailure(Exception exception, ShutdownReason? shutdownReason)
        {
            if (!string.IsNullOrWhiteSpace(lastTransportFailureMessage))
            {
                return true;
            }

            string baseMessage = GetBaseExceptionMessage(exception);
            if (baseMessage.IndexOf("Unable to connect to the remote server", StringComparison.OrdinalIgnoreCase) >= 0
                || baseMessage.IndexOf("operation was canceled", StringComparison.OrdinalIgnoreCase) >= 0
                || baseMessage.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                || baseMessage.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return shutdownReason.HasValue && shutdownReason.Value == ShutdownReason.Error;
        }

        private string BuildRetryStatusMessage(string sessionCode, int nextAttempt)
        {
            string action = state == BootstrapState.JoiningSession ? "Joining" : "Creating";
            return $"{action} private room {sessionCode}... retrying connection ({nextAttempt}/{MaxStartAttempts})";
        }

        private string BuildStartFailureMessage(Exception exception, ShutdownReason? shutdownReason)
        {
            if (!string.IsNullOrWhiteSpace(lastTransportFailureMessage))
            {
                return $"Failed to start Fusion session: {lastTransportFailureMessage}";
            }

            string baseMessage = GetBaseExceptionMessage(exception);
            if (baseMessage.IndexOf("Unable to connect to the remote server", StringComparison.OrdinalIgnoreCase) >= 0
                || baseMessage.IndexOf("operation was canceled", StringComparison.OrdinalIgnoreCase) >= 0
                || baseMessage.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Failed to start Fusion session: Photon Cloud websocket connection failed. Check internet, VPN, proxy, firewall, and try again.";
            }

            if (shutdownReason.HasValue && shutdownReason.Value != ShutdownReason.Ok)
            {
                return string.IsNullOrWhiteSpace(baseMessage)
                    ? $"Failed to start Fusion session: Fusion StartGame failed: {shutdownReason.Value}"
                    : $"Failed to start Fusion session: Fusion StartGame failed: {shutdownReason.Value} ({baseMessage})";
            }

            return string.IsNullOrWhiteSpace(baseMessage)
                ? "Failed to start Fusion session: Unknown startup error."
                : $"Failed to start Fusion session: {baseMessage}";
        }

        private async Task StartSessionAsync(string sessionCode)
        {
            sceneLoadRequested = false;
            activePlayers.Clear();
            CaptureBootstrapRenderSettings();
            ResetFailureDetails();

            if (!TryGetBootstrapSceneRef(out SceneRef bootstrapSceneRef))
            {
                return;
            }

            for (int attempt = 1; attempt <= MaxStartAttempts; attempt++)
            {
                ResetFailureDetails();

                try
                {
                    runner = CreateRunner();
                    SessionRuntime.SetSession(runner, sessionCode);

                    var startScene = new NetworkSceneInfo();
                    startScene.AddSceneRef(bootstrapSceneRef, LoadSceneMode.Single);

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
                        LogSessionSnapshot($"StartSessionAsync: StartGame failed attempt={attempt} shutdown={startResult.ShutdownReason}", runner);
                        if (attempt < MaxStartAttempts && IsTransientCloudConnectFailure(null, startResult.ShutdownReason))
                        {
                            await CleanupRunnerAsync(loadBootstrapScene: false);
                            statusMessage = BuildRetryStatusMessage(sessionCode, attempt + 1);
                            await Task.Delay(StartRetryDelayMilliseconds);
                            continue;
                        }

                        SetError(BuildStartFailureMessage(null, startResult.ShutdownReason));
                        await CleanupRunnerAsync(loadBootstrapScene: false);
                        return;
                    }

                    UpdateBootstrapStatus(runner);
                    LogSessionSnapshot("StartSessionAsync: StartGame completed", runner);
                    if (runner.IsSceneAuthority)
                    {
                        TryLoadGameplayScene();
                    }

                    return;
                }
                catch (Exception exception)
                {
                    LogSessionSnapshot($"StartSessionAsync: exception attempt={attempt} message={GetBaseExceptionMessage(exception)}", runner);
                    if (attempt < MaxStartAttempts && IsTransientCloudConnectFailure(exception, null))
                    {
                        await CleanupRunnerAsync(loadBootstrapScene: false);
                        statusMessage = BuildRetryStatusMessage(sessionCode, attempt + 1);
                        await Task.Delay(StartRetryDelayMilliseconds);
                        continue;
                    }

                    SetError(BuildStartFailureMessage(exception, null));
                    await CleanupRunnerAsync(loadBootstrapScene: false);
                    return;
                }
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
            LogSessionSnapshot($"Error: {message}", runner);
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

        private void CaptureBootstrapRenderSettings()
        {
            bootstrapSkyboxMaterial = RenderSettings.skybox;
            bootstrapAmbientMode = RenderSettings.ambientMode;
            bootstrapAmbientSkyColor = RenderSettings.ambientSkyColor;
            bootstrapAmbientEquatorColor = RenderSettings.ambientEquatorColor;
            bootstrapAmbientGroundColor = RenderSettings.ambientGroundColor;
            bootstrapAmbientIntensity = RenderSettings.ambientIntensity;
            bootstrapFogEnabled = RenderSettings.fog;
            bootstrapFogColor = RenderSettings.fogColor;
            bootstrapFogMode = RenderSettings.fogMode;
            bootstrapFogDensity = RenderSettings.fogDensity;
            bootstrapLinearFogStart = RenderSettings.fogStartDistance;
            bootstrapLinearFogEnd = RenderSettings.fogEndDistance;
            bootstrapSubtractiveShadowColor = RenderSettings.subtractiveShadowColor;
        }

        private static bool TryResolveSceneRef(string scenePath, out SceneRef sceneRef)
        {
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
            if (buildIndex < 0)
            {
                sceneRef = default;
                return false;
            }

            sceneRef = SceneRef.FromIndex(buildIndex);
            return true;
        }

        private bool TryGetBootstrapSceneRef(out SceneRef sceneRef, bool reportError = true)
        {
            if (TryResolveSceneRef(BootstrapScenePath, out sceneRef))
            {
                return true;
            }

            if (reportError)
            {
                SetError($"Bootstrap scene is missing from build settings: {BootstrapScenePath}");
            }

            return false;
        }

        private bool TryGetGameplaySceneRef(out SceneRef sceneRef, bool reportError = true)
        {
            if (TryResolveSceneRef(GameplayScenePath, out sceneRef))
            {
                return true;
            }

            if (reportError)
            {
                SetError($"Gameplay scene is missing from build settings: {GameplayScenePath}");
            }

            return false;
        }

        private static bool TryGetLoadedScene(string sceneName, out Scene scene)
        {
            scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                return true;
            }

            scene = SceneManager.GetActiveScene();
            return scene.IsValid() && scene.isLoaded && scene.name == sceneName;
        }

        private bool IsBootstrapSceneLoaded(NetworkRunner networkRunner)
        {
            return TryGetLoadedScene(BootstrapSceneName, out _);
        }

        private bool IsGameplaySceneLoaded(NetworkRunner networkRunner)
        {
            return TryGetLoadedScene(GameplaySceneName, out _);
        }

        private static Light FindDirectionalLightForScene(string sceneName)
        {
            if (!TryGetLoadedScene(sceneName, out Scene scene))
            {
                return null;
            }

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject == null)
                {
                    continue;
                }

                foreach (Light light in rootObject.GetComponentsInChildren<Light>(true))
                {
                    if (light != null && light.type == LightType.Directional)
                    {
                        return light;
                    }
                }
            }

            return null;
        }

        private void RestoreBootstrapRenderSettings(NetworkRunner networkRunner)
        {
            if (!IsBootstrapSceneLoaded(networkRunner) || IsGameplaySceneLoaded(networkRunner) || HasReadyMatch(networkRunner))
            {
                return;
            }

            RenderSettings.skybox = bootstrapSkyboxMaterial;
            RenderSettings.ambientMode = bootstrapAmbientMode;
            RenderSettings.ambientSkyColor = bootstrapAmbientSkyColor;
            RenderSettings.ambientEquatorColor = bootstrapAmbientEquatorColor;
            RenderSettings.ambientGroundColor = bootstrapAmbientGroundColor;
            RenderSettings.ambientIntensity = bootstrapAmbientIntensity;
            RenderSettings.fog = bootstrapFogEnabled;
            RenderSettings.fogColor = bootstrapFogColor;
            RenderSettings.fogMode = bootstrapFogMode;
            RenderSettings.fogDensity = bootstrapFogDensity;
            RenderSettings.fogStartDistance = bootstrapLinearFogStart;
            RenderSettings.fogEndDistance = bootstrapLinearFogEnd;
            RenderSettings.subtractiveShadowColor = bootstrapSubtractiveShadowColor;

            Light bootstrapDirectionalLight = FindDirectionalLightForScene(BootstrapSceneName);
            if (bootstrapDirectionalLight != null)
            {
                bootstrapDirectionalLight.enabled = true;
                RenderSettings.sun = bootstrapDirectionalLight;
            }

            DynamicGI.UpdateEnvironment();
        }

        private bool IsGameplayPresentationActive()
        {
            if (!IsGameplaySceneLoaded(runner))
            {
                return false;
            }

            return NetworkRaceManager.Instance != null || RunnerNetworkPlayer.LocalPlayer != null;
        }

        private bool ShouldShowBootstrapGui()
        {
            return !IsGameplayPresentationActive();
        }

        private void SyncActivePlayersFromRunner(NetworkRunner networkRunner)
        {
            activePlayers.Clear();

            if (networkRunner == null || !networkRunner.IsRunning)
            {
                return;
            }

            foreach (PlayerRef activePlayer in networkRunner.ActivePlayers)
            {
                activePlayers.Add(activePlayer);
            }
        }

        private bool HasReadyMatch(NetworkRunner networkRunner)
        {
            SyncActivePlayersFromRunner(networkRunner);

            if (networkRunner == null || !networkRunner.IsRunning || !networkRunner.IsPlayerValid(networkRunner.LocalPlayer))
            {
                return false;
            }

            if (activePlayers.Count != MaxPlayers || !activePlayers.Contains(networkRunner.LocalPlayer))
            {
                return false;
            }

            foreach (PlayerRef activePlayer in activePlayers)
            {
                if (activePlayer != networkRunner.LocalPlayer)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateBootstrapStatus(NetworkRunner networkRunner)
        {
            if (IsGameplaySceneLoaded(networkRunner))
            {
                if (IsGameplayPresentationActive())
                {
                    sceneLoadRequested = false;
                    state = BootstrapState.WaitingForSceneLoad;
                    statusMessage = "Race scene ready.";
                }

                return;
            }

            SyncActivePlayersFromRunner(networkRunner);

            if (networkRunner == null || !networkRunner.IsRunning || !SessionRuntime.HasSession)
            {
                state = BootstrapState.Ready;
                statusMessage = "Ready. Host a match or join with a code.";
                return;
            }

            bool readyMatch = HasReadyMatch(networkRunner);
            if (sceneLoadRequested && networkRunner.IsSceneAuthority && readyMatch)
            {
                state = BootstrapState.WaitingForSceneLoad;
                statusMessage = "Both players connected. Loading race scene...";
                return;
            }

            state = BootstrapState.WaitingForPlayer;
            if (networkRunner.IsSceneAuthority)
            {
                statusMessage = readyMatch
                    ? "Both players connected. Waiting for race start..."
                    : $"Waiting for player 2... ({activePlayers.Count}/{MaxPlayers})";
                return;
            }

            statusMessage = readyMatch
                ? "Connected. Waiting for host to start the race..."
                : $"Connected. Waiting for host to start the race... ({activePlayers.Count}/{MaxPlayers})";
        }

        private void LogSessionSnapshot(string context, NetworkRunner networkRunner)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SyncActivePlayersFromRunner(networkRunner);

            List<string> playerLabels = new List<string>(activePlayers.Count);
            foreach (PlayerRef activePlayer in activePlayers)
            {
                playerLabels.Add(activePlayer.ToString());
            }

            playerLabels.Sort(StringComparer.Ordinal);

            string playersLabel = playerLabels.Count > 0 ? string.Join(", ", playerLabels) : "none";
            string localPlayerLabel = networkRunner != null ? networkRunner.LocalPlayer.ToString() : "n/a";
            string sessionLabel = string.IsNullOrWhiteSpace(SessionRuntime.SessionCode) ? "none" : SessionRuntime.SessionCode;
            string sceneName = SceneManager.GetActiveScene().name;
            bool isSceneAuthority = networkRunner != null && networkRunner.IsRunning && networkRunner.IsSceneAuthority;
            bool bootstrapLoaded = IsBootstrapSceneLoaded(networkRunner);
            bool gameplayLoaded = IsGameplaySceneLoaded(networkRunner);

            Debug.Log(
                $"[SessionBootstrapper] {context} session={sessionLabel} local={localPlayerLabel} players=[{playersLabel}] scene={sceneName} bootstrapLoaded={bootstrapLoaded} gameplayLoaded={gameplayLoaded} sceneAuthority={isSceneAuthority} state={state} sceneLoadRequested={sceneLoadRequested}");
#endif
        }

        private void Update()
        {
            if (runner == null || !runner.IsRunning || leavingSession)
            {
                return;
            }

            RestoreBootstrapRenderSettings(runner);

            if (!IsGameplaySceneLoaded(runner)
                || HasReadyMatch(runner)
                || !runner.IsSceneAuthority
                || sceneLoadRequested
                || IsGameplayPresentationActive())
            {
                return;
            }

            sceneLoadRequested = true;
            state = BootstrapState.WaitingForPlayer;
            statusMessage = $"Gameplay scene appeared before both players were ready. Returning to {BootstrapSceneName}...";
            LogSessionSnapshot("Update: unexpected gameplay scene while waiting", runner);
            if (TryGetBootstrapSceneRef(out SceneRef bootstrapSceneRef))
            {
                runner.LoadScene(bootstrapSceneRef, LoadSceneMode.Single);
            }
        }

        private void TryLoadGameplayScene()
        {
            SyncActivePlayersFromRunner(runner);
            LogSessionSnapshot("TryLoadGameplayScene", runner);

            if (runner == null || !runner.IsRunning || !runner.IsSceneAuthority || sceneLoadRequested)
            {
                return;
            }

            if (!IsBootstrapSceneLoaded(runner) || !HasReadyMatch(runner))
            {
                UpdateBootstrapStatus(runner);
                return;
            }

            sceneLoadRequested = true;
            state = BootstrapState.WaitingForSceneLoad;
            statusMessage = "Both players connected. Loading race scene...";
            if (TryGetGameplaySceneRef(out SceneRef gameplaySceneRef))
            {
                runner.LoadScene(gameplaySceneRef, LoadSceneMode.Single);
            }
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
            SyncActivePlayersFromRunner(networkRunner);
            LogSessionSnapshot($"OnPlayerJoined player={player}", networkRunner);
            UpdateBootstrapStatus(networkRunner);

            if (networkRunner.IsSceneAuthority)
            {
                TryLoadGameplayScene();
            }
        }

        public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
        {
            SyncActivePlayersFromRunner(networkRunner);
            LogSessionSnapshot($"OnPlayerLeft player={player}", networkRunner);

            if (IsGameplaySceneLoaded(networkRunner) && networkRunner.IsSceneAuthority)
            {
                sceneLoadRequested = false;
                statusMessage = "The other player left the match. Returning to menu...";
                if (TryGetBootstrapSceneRef(out SceneRef bootstrapSceneRef))
                {
                    networkRunner.LoadScene(bootstrapSceneRef, LoadSceneMode.Single);
                }
                return;
            }

            UpdateBootstrapStatus(networkRunner);
        }

        public void OnInput(NetworkRunner networkRunner, NetworkInput input)
        {
            input.Set(RunnerInputAdapter.CaptureCurrentDevices());
        }

        public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason)
        {
            SessionRuntime.SetShutdownReason(shutdownReason);
            if (IsStartingSession())
            {
                if (shutdownReason != ShutdownReason.Ok)
                {
                    RecordTransportFailure($"Fusion shutdown during startup: {shutdownReason}", networkRunner);
                }

                return;
            }

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
            LogSessionSnapshot($"OnSceneLoadDone scene={activeSceneName}", networkRunner);
            if (IsGameplaySceneLoaded(networkRunner))
            {
                try
                {
                    EnsureGameplaySessionObjects(networkRunner);
                    sceneLoadRequested = false;
                    state = BootstrapState.WaitingForSceneLoad;
                    statusMessage = HasReadyMatch(networkRunner)
                        ? "Race scene ready."
                        : "Race scene loaded. Synchronizing player state...";
                }
                catch (Exception exception)
                {
                    SetError($"Failed to spawn Fusion session objects: {exception.Message}");
                }
            }
            else if (IsBootstrapSceneLoaded(networkRunner))
            {
                sceneLoadRequested = false;
                RestoreBootstrapRenderSettings(networkRunner);
                UpdateBootstrapStatus(networkRunner);
            }
        }

        public void OnConnectedToServer(NetworkRunner networkRunner) { }
        public void OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason)
        {
            string message = $"Disconnected from Photon: {reason}";
            if (IsStartingSession())
            {
                RecordTransportFailure(message, networkRunner);
                return;
            }

            SetError(message);
        }
        public void OnConnectRequest(NetworkRunner networkRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner networkRunner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            string message = $"Failed to connect: {reason}";
            if (IsStartingSession())
            {
                RecordTransportFailure(message, networkRunner);
                return;
            }

            SetError(message);
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
