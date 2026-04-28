using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public static class OnlineBuildSceneResolver
    {
        public static bool TryResolveSceneRef(string expectedSceneName, string expectedScenePath, out SceneRef sceneRef, out int buildIndex)
        {
            if (TryResolveBuildIndex(expectedSceneName, expectedScenePath, out buildIndex))
            {
                sceneRef = SceneRef.FromIndex(buildIndex);
                return true;
            }

            sceneRef = default;
            return false;
        }

        public static bool TryResolveBuildIndex(string expectedSceneName, string expectedScenePath, out int buildIndex)
        {
            int exactPathIndex = SceneUtility.GetBuildIndexByScenePath(expectedScenePath);
            if (IsBuildIndexValid(exactPathIndex, expectedSceneName, expectedScenePath))
            {
                buildIndex = exactPathIndex;
                return true;
            }

            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                if (TryGetBuildScenePath(index, out string scenePath) && PathsMatch(scenePath, expectedScenePath))
                {
                    buildIndex = index;
                    return true;
                }
            }

            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                if (TryGetBuildScenePath(index, out string scenePath) && NamesMatch(scenePath, expectedSceneName, expectedScenePath))
                {
                    buildIndex = index;
                    return true;
                }
            }

            buildIndex = -1;
            return false;
        }

        public static bool IsBuildIndexValid(int buildIndex, string expectedSceneName, string expectedScenePath)
        {
            return TryGetBuildScenePath(buildIndex, out string scenePath)
                && (PathsMatch(scenePath, expectedScenePath) || NamesMatch(scenePath, expectedSceneName, expectedScenePath));
        }

        public static string DescribeResolvedBuildIndex(string expectedSceneName, string expectedScenePath)
        {
            return TryResolveBuildIndex(expectedSceneName, expectedScenePath, out int buildIndex)
                ? buildIndex.ToString()
                : "missing";
        }

        public static string DescribeBuildScenes()
        {
            if (SceneManager.sceneCountInBuildSettings <= 0)
            {
                return "none";
            }

            StringBuilder builder = new StringBuilder();
            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                if (index > 0)
                {
                    builder.Append(" | ");
                }

                string scenePath = SceneUtility.GetScenePathByBuildIndex(index);
                builder.Append(index);
                builder.Append(':');
                builder.Append(string.IsNullOrWhiteSpace(scenePath) ? "<empty>" : scenePath);
            }

            return builder.ToString();
        }

        public static string GetBuildStamp()
        {
            string buildGuid = string.IsNullOrWhiteSpace(Application.buildGUID) ? "unknown" : Application.buildGUID;
            return $"{Application.version} | {buildGuid}";
        }

        private static bool TryGetBuildScenePath(int buildIndex, out string scenePath)
        {
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                scenePath = string.Empty;
                return false;
            }

            scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            return !string.IsNullOrWhiteSpace(scenePath);
        }

        private static bool PathsMatch(string actualPath, string expectedPath)
        {
            return string.Equals(NormalizePath(actualPath), NormalizePath(expectedPath), StringComparison.OrdinalIgnoreCase);
        }

        private static bool NamesMatch(string actualPath, string expectedSceneName, string expectedScenePath)
        {
            string actualName = Path.GetFileNameWithoutExtension(actualPath);
            string expectedName = string.IsNullOrWhiteSpace(expectedSceneName)
                ? Path.GetFileNameWithoutExtension(expectedScenePath)
                : expectedSceneName;

            return string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim();
        }
    }

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
        private const float GameplayLoadWatchdogTimeoutSeconds = 12f;

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

        private string statusMessage = "Ready. Host a match or join with a code.";
        private BootstrapState state = BootstrapState.Ready;
        private bool sceneLoadRequested;
        private bool leavingSession;
        private bool handlingShutdown;
        private NetworkRunner runner;
        private string lastTransportFailureMessage = string.Empty;
        private float gameplayLoadRequestedAtRealtime = -1f;
        private string pendingSceneLoadName = string.Empty;
        private int bootstrapSceneBuildIndex = -1;
        private SceneRef bootstrapSceneRef;
        private int gameplaySceneBuildIndex = -1;
        private SceneRef gameplaySceneRef;
        private bool raceManagerSpawnPending;
        private bool localPlayerSpawnPending;
        private PlayerRef pendingLocalPlayerSpawnRef = PlayerRef.None;
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
        private OnlineAudioDirector audioDirector;
        private bool showBootstrapDebug;
        private BootstrapMenuView bootstrapMenuView;

        public static SessionBootstrapper Instance { get; private set; }

        public NetworkRunner Runner => runner;
        public string CurrentSessionCode => SessionRuntime.SessionCode;
        public int CurrentPlayerCount => activePlayers.Count;

        public readonly struct BootstrapMenuSnapshot
        {
            public BootstrapMenuSnapshot(
                bool shouldShowMenu,
                string statusMessage,
                string sessionCode,
                int playerCount,
                int maxPlayers,
                bool canStartSession,
                bool showSessionInfo,
                bool canLeaveSession,
                bool showDebugPanel,
                string debugDetails)
            {
                ShouldShowMenu = shouldShowMenu;
                StatusMessage = statusMessage;
                SessionCode = sessionCode;
                PlayerCount = playerCount;
                MaxPlayers = maxPlayers;
                CanStartSession = canStartSession;
                ShowSessionInfo = showSessionInfo;
                CanLeaveSession = canLeaveSession;
                ShowDebugPanel = showDebugPanel;
                DebugDetails = debugDetails;
            }

            public bool ShouldShowMenu { get; }
            public string StatusMessage { get; }
            public string SessionCode { get; }
            public int PlayerCount { get; }
            public int MaxPlayers { get; }
            public bool CanStartSession { get; }
            public bool ShowSessionInfo { get; }
            public bool CanLeaveSession { get; }
            public bool ShowDebugPanel { get; }
            public string DebugDetails { get; }
        }

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
            EnsureAudioDirector().PlayMenuLoop();
            EnsureBootstrapMenuView();
            RefreshBootstrapMenu();
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

            EnsureAudioDirector().SetGameplayMusicPaused(false);
            _ = LeaveSessionInternalAsync(loadBootstrapScene: true, "Returned to menu.");
        }

        public BootstrapMenuSnapshot CreateMenuSnapshot()
        {
            bool hasRunningSession = HasRunningRunner(runner);
            string sessionCode = SessionRuntime.SessionCode;
            int playerCount = GetMenuPlayerCount(runner);
            bool showDebugPanel = Debug.isDebugBuild && showBootstrapDebug;
            return new BootstrapMenuSnapshot(
                ShouldShowBootstrapGui(),
                statusMessage,
                sessionCode,
                playerCount,
                MaxPlayers,
                CanStartNewSession(),
                hasRunningSession || !string.IsNullOrWhiteSpace(sessionCode),
                hasRunningSession && !leavingSession,
                showDebugPanel,
                showDebugPanel ? BuildBootstrapDebugDetails() : string.Empty);
        }

        public void SubmitHostFromMenu()
        {
            if (!CanStartNewSession())
            {
                return;
            }

            EnsureAudioDirector().PlayUiSelect();
            _ = CreatePrivateMatchAsync();
        }

        public void SubmitJoinFromMenu(string joinCode)
        {
            if (!CanStartNewSession() || string.IsNullOrWhiteSpace(joinCode))
            {
                return;
            }

            EnsureAudioDirector().PlayUiSelect();
            _ = JoinPrivateMatchAsync(joinCode);
        }

        public void SubmitLeaveFromMenu()
        {
            if (!HasRunningRunner(runner) || leavingSession)
            {
                return;
            }

            EnsureAudioDirector().PlayUiSelect();
            LeaveSession();
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
            ResetGameplaySpawnState();
            CaptureBootstrapRenderSettings();
            ResetFailureDetails();
            ClearGameplayLoadWatchdog();

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

                    LogSessionSnapshot("StartSessionAsync: StartGame completed", runner);
                    if (DrivesSceneLoad(runner))
                    {
                        TryLoadGameplayScene(runner);
                    }

                    UpdateBootstrapStatus(runner);

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
                ResetGameplaySpawnState();
                ClearGameplayLoadWatchdog();
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
            ClearGameplayLoadWatchdog();
            sceneLoadRequested = false;
            ResetGameplaySpawnState();

            if (loadBootstrapScene && SceneManager.GetActiveScene().name != BootstrapSceneName)
            {
                SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            }

            if (loadBootstrapScene || SceneManager.GetActiveScene().name == BootstrapSceneName)
            {
                EnsureAudioDirector().PlayMenuLoop();
            }
        }

        private void SetError(string message)
        {
            state = BootstrapState.Error;
            statusMessage = message;
            sceneLoadRequested = false;
            ClearGameplayLoadWatchdog();
            ResetGameplaySpawnState();
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

        private void ResetGameplaySpawnState()
        {
            raceManagerSpawnPending = false;
            localPlayerSpawnPending = false;
            pendingLocalPlayerSpawnRef = PlayerRef.None;
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

        private bool TryGetBootstrapSceneRef(out SceneRef sceneRef, bool reportError = true)
        {
            return TryGetSceneRef(
                ref bootstrapSceneBuildIndex,
                ref bootstrapSceneRef,
                BootstrapSceneName,
                BootstrapScenePath,
                "Bootstrap",
                out sceneRef,
                reportError);
        }

        private bool TryGetGameplaySceneRef(out SceneRef sceneRef, bool reportError = true)
        {
            return TryGetSceneRef(
                ref gameplaySceneBuildIndex,
                ref gameplaySceneRef,
                GameplaySceneName,
                GameplayScenePath,
                "Gameplay",
                out sceneRef,
                reportError);
        }

        private bool TryGetSceneRef(
            ref int cachedBuildIndex,
            ref SceneRef cachedSceneRef,
            string sceneName,
            string scenePath,
            string sceneLabel,
            out SceneRef sceneRef,
            bool reportError)
        {
            if (cachedSceneRef != default && OnlineBuildSceneResolver.IsBuildIndexValid(cachedBuildIndex, sceneName, scenePath))
            {
                sceneRef = cachedSceneRef;
                return true;
            }

            cachedBuildIndex = -1;
            cachedSceneRef = default;

            if (OnlineBuildSceneResolver.TryResolveSceneRef(sceneName, scenePath, out sceneRef, out int buildIndex))
            {
                cachedBuildIndex = buildIndex;
                cachedSceneRef = sceneRef;
                return true;
            }

            if (reportError)
            {
                LogSceneResolutionFailure(sceneLabel, sceneName, scenePath);
                SetError($"{sceneLabel} scene is missing from build settings: {scenePath}");
            }

            return false;
        }

        private void LogSceneResolutionFailure(string sceneLabel, string sceneName, string scenePath)
        {
            string bootstrapBuildIndex = OnlineBuildSceneResolver.DescribeResolvedBuildIndex(BootstrapSceneName, BootstrapScenePath);
            string gameplayBuildIndex = OnlineBuildSceneResolver.DescribeResolvedBuildIndex(GameplaySceneName, GameplayScenePath);
            string buildStamp = OnlineBuildSceneResolver.GetBuildStamp();
            string buildScenes = OnlineBuildSceneResolver.DescribeBuildScenes();

            Debug.LogError(
                $"[SessionBootstrapper] Failed to resolve {sceneLabel} scene name={sceneName} path={scenePath} buildStamp={buildStamp} bootstrapIndex={bootstrapBuildIndex} gameplayIndex={gameplayBuildIndex} buildScenes=[{buildScenes}]");
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

        private static bool IsSceneLoadedByUnity(string sceneName)
        {
            return TryGetLoadedScene(sceneName, out _);
        }

        private static bool TryGetRunnerSceneInfo(NetworkRunner networkRunner, out NetworkSceneInfo sceneInfo)
        {
            sceneInfo = default;
            return HasRunningRunner(networkRunner) && networkRunner.TryGetSceneInfo(out sceneInfo);
        }

        private static bool SceneInfoContains(NetworkSceneInfo sceneInfo, SceneRef sceneRef)
        {
            if (sceneRef == default)
            {
                return false;
            }

            for (int index = 0; index < sceneInfo.SceneCount; index++)
            {
                if (sceneInfo.Scenes[index] == sceneRef)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsBootstrapSceneLoadedByFusion(NetworkRunner networkRunner)
        {
            return TryGetBootstrapSceneRef(out SceneRef sceneRef, reportError: false)
                && TryGetRunnerSceneInfo(networkRunner, out NetworkSceneInfo sceneInfo)
                && SceneInfoContains(sceneInfo, sceneRef);
        }

        private bool IsGameplaySceneLoadedByFusion(NetworkRunner networkRunner)
        {
            return TryGetGameplaySceneRef(out SceneRef sceneRef, reportError: false)
                && TryGetRunnerSceneInfo(networkRunner, out NetworkSceneInfo sceneInfo)
                && SceneInfoContains(sceneInfo, sceneRef);
        }

        private bool IsBootstrapSceneLoaded(NetworkRunner networkRunner)
        {
            return IsBootstrapSceneLoadedByFusion(networkRunner);
        }

        private bool IsGameplaySceneLoaded(NetworkRunner networkRunner)
        {
            return IsGameplaySceneLoadedByFusion(networkRunner);
        }

        private static bool HasRunningRunner(NetworkRunner networkRunner)
        {
            return networkRunner != null && networkRunner.IsRunning;
        }

        private bool DrivesSceneLoad(NetworkRunner networkRunner)
        {
            return HasRunningRunner(networkRunner) && (networkRunner.IsSharedModeMasterClient || networkRunner.IsSceneAuthority);
        }

        private bool HasFullRoom(NetworkRunner networkRunner)
        {
            SyncActivePlayersFromRunner(networkRunner);
            return HasRunningRunner(networkRunner) && activePlayers.Count == MaxPlayers;
        }

        private bool IsLocalPlayerReady(NetworkRunner networkRunner)
        {
            return HasRunningRunner(networkRunner) && networkRunner.IsPlayerValid(networkRunner.LocalPlayer);
        }

        private void BeginGameplayLoadWatchdog(string sceneName)
        {
            gameplayLoadRequestedAtRealtime = Time.realtimeSinceStartup;
            pendingSceneLoadName = sceneName;
        }

        private void ClearGameplayLoadWatchdog()
        {
            gameplayLoadRequestedAtRealtime = -1f;
            pendingSceneLoadName = string.Empty;
        }

        private bool IsGameplayLoadPending()
        {
            return gameplayLoadRequestedAtRealtime >= 0f;
        }

        private bool HasGameplayLoadTimedOut()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return gameplayLoadRequestedAtRealtime >= 0f
                && Time.realtimeSinceStartup - gameplayLoadRequestedAtRealtime >= GameplayLoadWatchdogTimeoutSeconds;
#else
            return false;
#endif
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

        private static string DescribeFusionSceneInfo(NetworkRunner networkRunner)
        {
            return TryGetRunnerSceneInfo(networkRunner, out NetworkSceneInfo sceneInfo)
                ? sceneInfo.ToString()
                : "invalid";
        }

        private void RestoreBootstrapRenderSettings(NetworkRunner networkRunner)
        {
            if (!IsBootstrapSceneLoaded(networkRunner) || IsGameplaySceneLoaded(networkRunner))
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

        private bool HasPendingGameplaySpawns()
        {
            return raceManagerSpawnPending || localPlayerSpawnPending;
        }

        private bool IsGameplayPresentationActive(NetworkRunner networkRunner)
        {
            if (!IsGameplaySceneLoaded(networkRunner))
            {
                return false;
            }

            if (!OnlineSceneRuntime.IsGameplaySceneInitialized(networkRunner))
            {
                return false;
            }

            RunnerNetworkPlayer localPlayer = RunnerNetworkPlayer.LocalPlayer;
            if (NetworkRaceManager.Instance == null || localPlayer == null)
            {
                return false;
            }

            if (!HasRunningRunner(networkRunner) || !networkRunner.IsPlayerValid(networkRunner.LocalPlayer))
            {
                return false;
            }

            return localPlayer.Runner == networkRunner
                && localPlayer.IsLocalControlled
                && localPlayer.Object != null
                && localPlayer.Object.IsValid
                && localPlayer.HasActiveCameraBinding;
        }

        private bool ShouldShowBootstrapGui()
        {
            return !IsGameplayPresentationActive(runner);
        }

        private int GetMenuPlayerCount(NetworkRunner networkRunner)
        {
            if (!HasRunningRunner(networkRunner))
            {
                return activePlayers.Count;
            }

            int count = 0;
            foreach (PlayerRef activePlayer in networkRunner.ActivePlayers)
            {
                if (activePlayer != PlayerRef.None)
                {
                    count++;
                }
            }

            return count;
        }

        private void EnsureBootstrapMenuView()
        {
            if (bootstrapMenuView != null)
            {
                return;
            }

            bootstrapMenuView = GetComponentInChildren<BootstrapMenuView>(true);
            if (bootstrapMenuView == null)
            {
                GameObject menuObject = new GameObject("Bootstrap UI Toolkit Menu");
                menuObject.transform.SetParent(transform, false);
                menuObject.AddComponent<UnityEngine.UIElements.UIDocument>();
                bootstrapMenuView = menuObject.AddComponent<BootstrapMenuView>();
            }

            bootstrapMenuView.Initialize(this);
        }

        private void RefreshBootstrapMenu()
        {
            EnsureBootstrapMenuView();
            bootstrapMenuView.Refresh(CreateMenuSnapshot());
        }

        private string BuildBootstrapDebugDetails()
        {
            NetworkRunner currentRunner = runner;
            string buildStamp = OnlineBuildSceneResolver.GetBuildStamp();
            string bootstrapBuildIndex = OnlineBuildSceneResolver.DescribeResolvedBuildIndex(BootstrapSceneName, BootstrapScenePath);
            string gameplayBuildIndex = OnlineBuildSceneResolver.DescribeResolvedBuildIndex(GameplaySceneName, GameplayScenePath);
            bool hasRunner = HasRunningRunner(currentRunner);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Build Stamp: {buildStamp}");
            builder.AppendLine($"Active Unity Scene: {SceneManager.GetActiveScene().name}");
            builder.AppendLine($"Fusion Scene Info: {DescribeFusionSceneInfo(currentRunner)}");
            builder.AppendLine($"Bootstrap Build Index: {bootstrapBuildIndex}");
            builder.AppendLine($"Gameplay Build Index: {gameplayBuildIndex}");
            builder.AppendLine($"Bootstrap Loaded (Fusion): {IsBootstrapSceneLoadedByFusion(currentRunner)}");
            builder.AppendLine($"Bootstrap Loaded (Unity): {IsSceneLoadedByUnity(BootstrapSceneName)}");
            builder.AppendLine($"Gameplay Loaded (Fusion): {IsGameplaySceneLoadedByFusion(currentRunner)}");
            builder.AppendLine($"Gameplay Loaded (Unity): {IsSceneLoadedByUnity(GameplaySceneName)}");
            builder.AppendLine($"Host Drives Scene Load: {DrivesSceneLoad(currentRunner)}");
            builder.AppendLine($"Scene Authority: {hasRunner && currentRunner.IsSceneAuthority}");
            builder.AppendLine($"Master Client: {hasRunner && currentRunner.IsSharedModeMasterClient}");
            builder.AppendLine($"Full Room: {GetMenuPlayerCount(currentRunner) >= MaxPlayers}");
            builder.AppendLine($"Local Player Valid: {IsLocalPlayerReady(currentRunner)}");
            builder.AppendLine($"Scene Load Requested: {sceneLoadRequested}");
            builder.AppendLine($"Gameplay Load Pending: {IsGameplayLoadPending()}");
            builder.Append($"Pending Gameplay Spawns: {HasPendingGameplaySpawns()}");
            return builder.ToString();
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

        private void UpdateBootstrapStatus(NetworkRunner networkRunner)
        {
            if (IsGameplaySceneLoaded(networkRunner))
            {
                if (IsGameplayPresentationActive(networkRunner))
                {
                    sceneLoadRequested = false;
                    ClearGameplayLoadWatchdog();
                    state = BootstrapState.WaitingForSceneLoad;
                    statusMessage = "Race scene ready.";
                }
                else
                {
                    state = BootstrapState.WaitingForSceneLoad;
                    statusMessage = HasPendingGameplaySpawns()
                        ? "Gameplay scene loaded. Spawning network objects..."
                        : DrivesSceneLoad(networkRunner)
                            ? "Gameplay scene loaded. Finalizing race start..."
                            : "Gameplay scene loaded. Waiting for race to initialize...";
                }

                return;
            }

            SyncActivePlayersFromRunner(networkRunner);

            if (!HasRunningRunner(networkRunner))
            {
                state = BootstrapState.Ready;
                statusMessage = "Ready. Host a match or join with a code.";
                return;
            }

            bool fullRoom = HasFullRoom(networkRunner);
            bool hostDrivesSceneLoad = DrivesSceneLoad(networkRunner);
            if (sceneLoadRequested && hostDrivesSceneLoad)
            {
                state = BootstrapState.WaitingForSceneLoad;
                statusMessage = "Both players connected. Loading race scene...";
                return;
            }

            state = BootstrapState.WaitingForPlayer;
            if (hostDrivesSceneLoad)
            {
                statusMessage = fullRoom
                    ? "Both players connected. Starting race..."
                    : $"Waiting for player 2... ({activePlayers.Count}/{MaxPlayers})";
                return;
            }

            statusMessage = fullRoom
                ? "Connected. Waiting for host to start the race..."
                : $"Connected. Waiting for host to start the race... ({activePlayers.Count}/{MaxPlayers})";
        }

        private void EnsureGameplayPresentationReady(NetworkRunner networkRunner, string context)
        {
            if (!IsGameplaySceneLoaded(networkRunner))
            {
                return;
            }

            LogSessionSnapshot($"{context}: ensuring gameplay presentation", networkRunner);

            try
            {
                OnlineSceneRuntime.EnsureGameplaySceneReady(networkRunner);
                EnsureGameplaySessionObjects(networkRunner);
            }
            catch (Exception exception)
            {
                SetError($"Failed to initialize gameplay scene: {exception.Message}");
                return;
            }

            if (IsGameplayPresentationActive(networkRunner))
            {
                sceneLoadRequested = false;
                ClearGameplayLoadWatchdog();
                state = BootstrapState.WaitingForSceneLoad;
                statusMessage = "Race scene ready.";
                EnsureAudioDirector().PlayGameplayLoop();
                LogSessionSnapshot($"{context}: gameplay presentation ready", networkRunner);
                return;
            }

            state = BootstrapState.WaitingForSceneLoad;
            statusMessage = HasPendingGameplaySpawns()
                ? "Gameplay scene loaded. Spawning network objects..."
                : HasFullRoom(networkRunner)
                    ? "Gameplay scene loaded. Finalizing race start..."
                    : "Gameplay scene loaded. Waiting for race to initialize...";
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
            string unitySceneName = SceneManager.GetActiveScene().name;
            bool isSceneAuthority = networkRunner != null && networkRunner.IsRunning && networkRunner.IsSceneAuthority;
            bool isMasterClient = networkRunner != null && networkRunner.IsRunning && networkRunner.IsSharedModeMasterClient;
            bool hostDrivesSceneLoad = DrivesSceneLoad(networkRunner);
            bool bootstrapLoaded = IsBootstrapSceneLoaded(networkRunner);
            bool gameplayLoaded = IsGameplaySceneLoaded(networkRunner);
            bool bootstrapLoadedByFusion = IsBootstrapSceneLoadedByFusion(networkRunner);
            bool gameplayLoadedByFusion = IsGameplaySceneLoadedByFusion(networkRunner);
            bool bootstrapLoadedByUnity = IsSceneLoadedByUnity(BootstrapSceneName);
            bool gameplayLoadedByUnity = IsSceneLoadedByUnity(GameplaySceneName);
            string bootstrapBuildIndex = OnlineBuildSceneResolver.DescribeResolvedBuildIndex(BootstrapSceneName, BootstrapScenePath);
            string gameplayBuildIndex = OnlineBuildSceneResolver.DescribeResolvedBuildIndex(GameplaySceneName, GameplayScenePath);
            string buildStamp = OnlineBuildSceneResolver.GetBuildStamp();
            bool fullRoom = networkRunner != null && networkRunner.IsRunning && activePlayers.Count == MaxPlayers;
            bool localPlayerValid = networkRunner != null && networkRunner.IsRunning && networkRunner.IsPlayerValid(networkRunner.LocalPlayer);
            bool hasActiveRunnerSession = SessionRuntime.Runner != null && SessionRuntime.Runner.IsRunning;
            string fusionSceneInfo = DescribeFusionSceneInfo(networkRunner);

            Debug.Log(
                $"[SessionBootstrapper] {context} session={sessionLabel} local={localPlayerLabel} players=[{playersLabel}] unityScene={unitySceneName} fusionSceneInfo={fusionSceneInfo} buildStamp={buildStamp} bootstrapIndex={bootstrapBuildIndex} gameplayIndex={gameplayBuildIndex} bootstrapLoaded={bootstrapLoaded} gameplayLoaded={gameplayLoaded} bootstrapFusion={bootstrapLoadedByFusion} gameplayFusion={gameplayLoadedByFusion} bootstrapUnity={bootstrapLoadedByUnity} gameplayUnity={gameplayLoadedByUnity} sceneAuthority={isSceneAuthority} masterClient={isMasterClient} hostDriver={hostDrivesSceneLoad} fullRoom={fullRoom} localPlayerValid={localPlayerValid} state={state} sceneLoadRequested={sceneLoadRequested} gameplayLoadPending={IsGameplayLoadPending()} pendingSpawns={HasPendingGameplaySpawns()} activeRunnerSession={hasActiveRunnerSession}");
#endif
        }

        private void Update()
        {
            if (Debug.isDebugBuild && Input.GetKeyDown(KeyCode.F3))
            {
                showBootstrapDebug = !showBootstrapDebug;
            }

            RefreshBootstrapMenu();

            if (runner == null || !runner.IsRunning || leavingSession)
            {
                return;
            }

            RestoreBootstrapRenderSettings(runner);

            if (HasGameplayLoadTimedOut() && !IsGameplayPresentationActive(runner))
            {
                LogSessionSnapshot("Update: gameplay load watchdog timed out", runner);
                SetError($"Timed out waiting for '{pendingSceneLoadName}' to become ready.");
                return;
            }

            if (IsGameplaySceneLoaded(runner))
            {
                if (!IsGameplayPresentationActive(runner))
                {
                    EnsureGameplayPresentationReady(runner, "Update");
                }
                else
                {
                    sceneLoadRequested = false;
                    ClearGameplayLoadWatchdog();
                }

                return;
            }

            if (DrivesSceneLoad(runner) && HasFullRoom(runner) && !sceneLoadRequested)
            {
                statusMessage = "Both players connected. Starting race...";
                TryLoadGameplayScene(runner);
            }
        }

        private void TryLoadGameplayScene(NetworkRunner networkRunner)
        {
            SyncActivePlayersFromRunner(networkRunner);
            LogSessionSnapshot("TryLoadGameplayScene", networkRunner);

            if (!HasRunningRunner(networkRunner))
            {
                return;
            }

            if (IsGameplaySceneLoaded(networkRunner))
            {
                EnsureGameplayPresentationReady(networkRunner, "TryLoadGameplayScene");
                UpdateBootstrapStatus(networkRunner);
                return;
            }

            if (!DrivesSceneLoad(networkRunner))
            {
                LogSessionSnapshot("TryLoadGameplayScene: local peer does not drive scene changes", networkRunner);
                return;
            }

            if (sceneLoadRequested)
            {
                LogSessionSnapshot("TryLoadGameplayScene: gameplay load already pending", networkRunner);
                return;
            }

            if (!HasFullRoom(networkRunner))
            {
                LogSessionSnapshot("TryLoadGameplayScene: room not full yet", networkRunner);
                UpdateBootstrapStatus(networkRunner);
                return;
            }

            sceneLoadRequested = true;
            BeginGameplayLoadWatchdog(GameplaySceneName);
            state = BootstrapState.WaitingForSceneLoad;
            statusMessage = "Both players connected. Loading race scene...";
            if (TryGetGameplaySceneRef(out SceneRef gameplaySceneRef))
            {
                LogSessionSnapshot("TryLoadGameplayScene: calling runner.LoadScene(Joc)", networkRunner);
                networkRunner.LoadScene(gameplaySceneRef, LoadSceneMode.Single);
                return;
            }

            sceneLoadRequested = false;
            ClearGameplayLoadWatchdog();
        }

        private void EnsureGameplaySessionObjects(NetworkRunner networkRunner)
        {
            if (DrivesSceneLoad(networkRunner))
            {
                NetworkObject raceManagerPrefab = Resources.Load<NetworkObject>(RaceManagerPrefabResourcePath);
                if (raceManagerPrefab == null)
                {
                    throw new InvalidOperationException($"Missing Resources/{RaceManagerPrefabResourcePath}.prefab required for Fusion gameplay startup.");
                }

                if (NetworkRaceManager.Instance != null)
                {
                    raceManagerSpawnPending = false;
                }
                else if (!raceManagerSpawnPending)
                {
                    raceManagerSpawnPending = true;
                    LogSessionSnapshot("EnsureGameplaySessionObjects: spawning race manager async", networkRunner);
                    networkRunner.SpawnAsync(
                        raceManagerPrefab,
                        flags: NetworkSpawnFlags.SharedModeStateAuthMasterClient,
                        onCompleted: result => HandleRaceManagerSpawnCompleted(networkRunner, result));
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
                localPlayerSpawnPending = false;
                pendingLocalPlayerSpawnRef = PlayerRef.None;
                return;
            }

            if (pendingLocalPlayerSpawnRef != PlayerRef.None && pendingLocalPlayerSpawnRef != networkRunner.LocalPlayer)
            {
                localPlayerSpawnPending = false;
                pendingLocalPlayerSpawnRef = PlayerRef.None;
            }

            if (localPlayerSpawnPending)
            {
                return;
            }

            pendingLocalPlayerSpawnRef = networkRunner.LocalPlayer;
            localPlayerSpawnPending = true;
            LogSessionSnapshot("EnsureGameplaySessionObjects: spawning local player async", networkRunner);
            networkRunner.SpawnAsync(
                playerPrefab,
                position: Vector3.zero,
                rotation: Quaternion.identity,
                inputAuthority: networkRunner.LocalPlayer,
                flags: NetworkSpawnFlags.SharedModeStateAuthLocalPlayer,
                onCompleted: result => HandleLocalPlayerSpawnCompleted(networkRunner, networkRunner.LocalPlayer, result));
        }

        private void HandleRaceManagerSpawnCompleted(NetworkRunner sourceRunner, NetworkSpawnOp result)
        {
            raceManagerSpawnPending = false;

            if (!HasRunningRunner(sourceRunner) || runner != sourceRunner || leavingSession)
            {
                return;
            }

            LogSessionSnapshot($"HandleRaceManagerSpawnCompleted status={result.Status}", sourceRunner);

            if (!result.IsSpawned || result.Object == null)
            {
                SetError($"Failed to spawn NetworkRaceManager: {result.Status}");
                return;
            }

            EnsureGameplayPresentationReady(sourceRunner, "HandleRaceManagerSpawnCompleted");
        }

        private void HandleLocalPlayerSpawnCompleted(NetworkRunner sourceRunner, PlayerRef playerRef, NetworkSpawnOp result)
        {
            if (pendingLocalPlayerSpawnRef == playerRef)
            {
                pendingLocalPlayerSpawnRef = PlayerRef.None;
            }

            localPlayerSpawnPending = false;

            if (!HasRunningRunner(sourceRunner) || runner != sourceRunner || leavingSession)
            {
                return;
            }

            LogSessionSnapshot($"HandleLocalPlayerSpawnCompleted status={result.Status} player={playerRef}", sourceRunner);

            if (!result.IsSpawned || result.Object == null)
            {
                SetError($"Failed to spawn RunnerNetworkPlayer: {result.Status}");
                return;
            }

            EnsureGameplayPresentationReady(sourceRunner, "HandleLocalPlayerSpawnCompleted");
        }

        public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef player)
        {
            SyncActivePlayersFromRunner(networkRunner);
            LogSessionSnapshot($"OnPlayerJoined player={player}", networkRunner);

            if (DrivesSceneLoad(networkRunner))
            {
                TryLoadGameplayScene(networkRunner);
            }

            UpdateBootstrapStatus(networkRunner);
        }

        public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
        {
            SyncActivePlayersFromRunner(networkRunner);
            LogSessionSnapshot($"OnPlayerLeft player={player}", networkRunner);

            if (IsGameplaySceneLoaded(networkRunner) && DrivesSceneLoad(networkRunner))
            {
                sceneLoadRequested = false;
                ClearGameplayLoadWatchdog();
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
                EnsureGameplayPresentationReady(networkRunner, "OnSceneLoadDone");
            }
            else if (IsBootstrapSceneLoaded(networkRunner))
            {
                sceneLoadRequested = false;
                ClearGameplayLoadWatchdog();
                RestoreBootstrapRenderSettings(networkRunner);
                EnsureAudioDirector().PlayMenuLoop();
                UpdateBootstrapStatus(networkRunner);
            }
        }

        private OnlineAudioDirector EnsureAudioDirector()
        {
            if (audioDirector == null)
            {
                audioDirector = GetComponent<OnlineAudioDirector>();
                if (audioDirector == null)
                {
                    audioDirector = gameObject.AddComponent<OnlineAudioDirector>();
                }
            }

            return audioDirector;
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
        public void OnSceneLoadStart(NetworkRunner networkRunner)
        {
            LogSessionSnapshot("OnSceneLoadStart", networkRunner);
        }
        public void OnObjectEnterAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectExitAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
    }
}
