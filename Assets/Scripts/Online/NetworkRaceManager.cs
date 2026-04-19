using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkRaceManager : NetworkBehaviour
    {
        private const float RoundAdvanceDelaySeconds = 3f;
        private const float MatchReturnDelaySeconds = 4f;
        private const string BootstrapSceneName = "Bootstrap";
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        [Networked] private RaceRoundState NetworkRoundState { get; set; }
        [Networked] private TickTimer PhaseTimer { get; set; }

        private LevelCourseDefinition[] courses;
        private ObstacleManager obstacleManager;
        private RaceRoundState cachedRoundState = RaceRoundState.WaitingForPlayers;
        private RaceRoundState lastAppliedObstacleRoundState = RaceRoundState.WaitingForPlayers;
        private bool hasAppliedObstacleRoundState;

        public static NetworkRaceManager Instance { get; private set; }
        public RaceRoundState RoundState => Object != null && Object.IsValid ? NetworkRoundState : cachedRoundState;

        private void Awake()
        {
            courses = LegacySceneAdapter.BuildCourses().ToArray();
        }

        public override void Spawned()
        {
            if (Instance != null && Instance != this)
            {
                Runner.Despawn(Object);
                return;
            }

            Instance = this;
            EnsureObstacleManagerReference();

            if (HasStateAuthority)
            {
                NetworkRoundState = RaceRoundState.WaitingForPlayers;
                PhaseTimer = TickTimer.None;
            }

            cachedRoundState = RoundState;
            ApplyObservedRoundState(RoundState);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
            {
                Instance = null;
            }

            obstacleManager = null;
            hasAppliedObstacleRoundState = false;
        }

        public override void FixedUpdateNetwork()
        {
            cachedRoundState = RoundState;

            if (!HasStateAuthority)
            {
                return;
            }

            int connectedPlayers = Runner.ActivePlayers.Count();
            if (connectedPlayers < 2)
            {
                NetworkRoundState = RaceRoundState.WaitingForPlayers;
                PhaseTimer = TickTimer.None;
                return;
            }

            if (!AllPlayersSpawned())
            {
                return;
            }

            if (NetworkRoundState.Phase == RaceRoundPhase.WaitingForPlayers)
            {
                StartRound(1);
                return;
            }

            if (PhaseTimer.IsRunning && PhaseTimer.Expired(Runner))
            {
                AdvancePhaseTimer();
            }
        }

        public override void Render()
        {
            cachedRoundState = RoundState;
            ApplyObservedRoundState(cachedRoundState);
        }

        public LevelCourseDefinition GetCurrentCourse()
        {
            if (courses == null || courses.Length == 0)
            {
                return null;
            }

            int index = Mathf.Clamp(RoundState.LevelIndex - 1, 0, courses.Length - 1);
            return courses[index];
        }

        public bool IsPlayerWinner(RunnerNetworkPlayer player)
        {
            return player != null
                && player.OwnerPlayer != PlayerRef.None
                && RoundState.WinnerPlayer != PlayerRef.None
                && player.OwnerPlayer == RoundState.WinnerPlayer;
        }

        public bool IsPlayerLoser(RunnerNetworkPlayer player)
        {
            return player != null
                && player.OwnerPlayer != PlayerRef.None
                && RoundState.WinnerPlayer != PlayerRef.None
                && player.OwnerPlayer != RoundState.WinnerPlayer;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ReportPlayerFinished(PlayerRef player, float pathState)
        {
            if (!HasStateAuthority || NetworkRoundState.Phase != RaceRoundPhase.Racing || !Runner.IsPlayerValid(player))
            {
                return;
            }

            RunnerNetworkPlayer winner = FindPlayerFor(player);
            LevelCourseDefinition course = GetCurrentCourse();
            if (winner == null || course == null || pathState < course.FinishDistance)
            {
                return;
            }

            NetworkRoundState = new RaceRoundState(
                NetworkRoundState.LevelIndex,
                NetworkRoundState.LevelIndex >= courses.Length ? RaceRoundPhase.MatchComplete : RaceRoundPhase.RoundResult,
                Runner.Tick,
                player);

            PhaseTimer = TickTimer.CreateFromSeconds(
                Runner,
                NetworkRoundState.Phase == RaceRoundPhase.MatchComplete ? MatchReturnDelaySeconds : RoundAdvanceDelaySeconds);
        }

        private void AdvancePhaseTimer()
        {
            PhaseTimer = TickTimer.None;

            if (NetworkRoundState.Phase == RaceRoundPhase.MatchComplete)
            {
                if (Runner.IsSharedModeMasterClient || Runner.IsSceneAuthority)
                {
                    if (OnlineBuildSceneResolver.TryResolveSceneRef(
                        BootstrapSceneName,
                        BootstrapScenePath,
                        out SceneRef bootstrapSceneRef,
                        out _))
                    {
                        Runner.LoadScene(bootstrapSceneRef, LoadSceneMode.Single);
                    }
                    else
                    {
                        Debug.LogError(
                            $"[NetworkRaceManager] Failed to resolve Bootstrap scene for match return. buildStamp={OnlineBuildSceneResolver.GetBuildStamp()} buildScenes=[{OnlineBuildSceneResolver.DescribeBuildScenes()}]");
                    }
                }

                return;
            }

            if (NetworkRoundState.Phase == RaceRoundPhase.RoundResult)
            {
                StartRound(NetworkRoundState.LevelIndex + 1);
            }
        }

        private void StartRound(int levelIndex)
        {
            NetworkRoundState = new RaceRoundState(levelIndex, RaceRoundPhase.Racing, Runner.Tick, PlayerRef.None);
            PhaseTimer = TickTimer.None;
        }

        private void ApplyObservedRoundState(RaceRoundState current)
        {
            if (current.LevelIndex <= 0 || !EnsureObstacleManagerReference())
            {
                return;
            }

            if (hasAppliedObstacleRoundState
                && current.LevelIndex == lastAppliedObstacleRoundState.LevelIndex
                && current.Phase == lastAppliedObstacleRoundState.Phase
                && current.RoundStartTick == lastAppliedObstacleRoundState.RoundStartTick)
            {
                return;
            }

            bool enteringNewRacingRound = current.Phase == RaceRoundPhase.Racing
                && (!hasAppliedObstacleRoundState
                    || lastAppliedObstacleRoundState.Phase != RaceRoundPhase.Racing
                    || current.LevelIndex != lastAppliedObstacleRoundState.LevelIndex
                    || current.RoundStartTick != lastAppliedObstacleRoundState.RoundStartTick);

            if (enteringNewRacingRound)
            {
                obstacleManager.ResetForRound(current.LevelIndex);
            }

            obstacleManager.SetActiveLevel(current.LevelIndex);
            lastAppliedObstacleRoundState = current;
            hasAppliedObstacleRoundState = true;
        }

        private bool EnsureObstacleManagerReference()
        {
            if (obstacleManager != null)
            {
                return true;
            }

            obstacleManager = LegacySceneAdapter.ObstacleManager ?? UnityEngine.Object.FindAnyObjectByType<ObstacleManager>(FindObjectsInactive.Include);
            return obstacleManager != null;
        }

        private bool AllPlayersSpawned()
        {
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                RunnerNetworkPlayer networkPlayer = FindPlayerFor(player);
                if (networkPlayer == null || networkPlayer.SpawnSlot == RunnerSpawnSlot.None)
                {
                    return false;
                }
            }

            return true;
        }

        private RunnerNetworkPlayer FindPlayerFor(PlayerRef player)
        {
            if (!Runner.IsPlayerValid(player))
            {
                return null;
            }

            foreach (RunnerNetworkPlayer networkPlayer in UnityEngine.Object.FindObjectsByType<RunnerNetworkPlayer>(FindObjectsInactive.Exclude))
            {
                if (networkPlayer == null || networkPlayer.Object == null || !networkPlayer.Object.IsValid)
                {
                    continue;
                }

                if (networkPlayer.OwnerPlayer == player)
                {
                    return networkPlayer;
                }
            }

            return null;
        }
    }
}
