using System.Collections.Generic;
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

        [Networked] private RaceRoundState NetworkRoundState { get; set; }
        [Networked] private TickTimer PhaseTimer { get; set; }

        private LevelCourseDefinition[] courses;
        private ObstacleManager obstacleManager;
        private RaceRoundState cachedRoundState = RaceRoundState.WaitingForPlayers;

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
            obstacleManager = LegacySceneAdapter.ObstacleManager ?? UnityEngine.Object.FindAnyObjectByType<ObstacleManager>(FindObjectsInactive.Include);
            cachedRoundState = NetworkRoundState;

            if (HasStateAuthority)
            {
                NetworkRoundState = RaceRoundState.WaitingForPlayers;
                PhaseTimer = TickTimer.None;
            }

            ApplyObservedRoundState(RoundState);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void FixedUpdateNetwork()
        {
            cachedRoundState = RoundState;
            ApplyObservedRoundState(cachedRoundState);

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
                && RoundState.WinnerPlayer != PlayerRef.None
                && player.OwnerPlayer == RoundState.WinnerPlayer;
        }

        public bool IsPlayerLoser(RunnerNetworkPlayer player)
        {
            return player != null
                && RoundState.WinnerPlayer != PlayerRef.None
                && player.OwnerPlayer != RoundState.WinnerPlayer;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ReportPlayerFinished(PlayerRef player, float pathState)
        {
            if (!HasStateAuthority || NetworkRoundState.Phase != RaceRoundPhase.Racing)
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
                if (Runner.IsSceneAuthority)
                {
                    Runner.LoadScene(SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Bootstrap.unity")), LoadSceneMode.Single);
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

            obstacleManager?.ResetForRound(levelIndex);
            obstacleManager?.SetActiveLevel(levelIndex);
        }

        private void ApplyObservedRoundState(RaceRoundState current)
        {
            if (current.LevelIndex > 0)
            {
                obstacleManager?.SetActiveLevel(current.LevelIndex);
            }
        }

        private bool AllPlayersSpawned()
        {
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (!Runner.TryGetPlayerObject(player, out NetworkObject playerObject) || playerObject == null)
                {
                    return false;
                }
            }

            return true;
        }

        private RunnerNetworkPlayer FindPlayerFor(PlayerRef player)
        {
            if (!Runner.TryGetPlayerObject(player, out NetworkObject playerObject) || playerObject == null)
            {
                return null;
            }

            return playerObject.GetComponent<RunnerNetworkPlayer>();
        }
    }
}
