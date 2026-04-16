using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public class NetworkRaceManager : MonoBehaviour
    {
        private readonly List<RunnerNetworkPlayer> registeredPlayers = new();
        private LevelCourseDefinition[] courses;
        private ObstacleManager obstacleManager;
        private RaceRoundState localRoundState = new(1, RaceRoundPhase.WaitingForPlayers, 0d);

        public static NetworkRaceManager Instance { get; private set; }
        public RaceRoundState RoundState => GetObservedRoundState();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            courses = new List<LevelCourseDefinition>(LegacySceneAdapter.BuildCourses()).ToArray();
            obstacleManager = LegacySceneAdapter.ObstacleManager;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void RegisterPlayer(RunnerNetworkPlayer player)
        {
            if (!registeredPlayers.Contains(player))
            {
                registeredPlayers.Add(player);
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                AssignSpawnSlotsServer();
                TryStartMatchServer();
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

        public void NotifyPlayerFinishedServer(RunnerNetworkPlayer winner)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || RoundState.Phase != RaceRoundPhase.Racing)
            {
                return;
            }

            RaceRoundState state = RoundState;
            state.Phase = state.LevelIndex >= courses.Length ? RaceRoundPhase.MatchComplete : RaceRoundPhase.RoundResult;
            state.WinnerClientId = winner.OwnerClientId;
            BroadcastRoundStateServer(state);

            foreach (RunnerNetworkPlayer player in registeredPlayers)
            {
                bool isWinner = player == winner;
                player.ApplyRoundResultServer(isWinner, !isWinner);
            }

            if (state.LevelIndex >= courses.Length)
            {
                StartCoroutine(ReturnToBootstrapAfterMatchRoutine());
            }
            else
            {
                StartCoroutine(AdvanceToNextRoundRoutine(state.LevelIndex + 1));
            }
        }

        private void TryStartMatchServer()
        {
            registeredPlayers.RemoveAll(player => player == null);
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || registeredPlayers.Count < 2 || courses == null || courses.Length == 0)
            {
                return;
            }

            StartRoundServer(1);
        }

        private void AssignSpawnSlotsServer()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            registeredPlayers.RemoveAll(player => player == null);
            ulong serverClientId = NetworkManager.ServerClientId;
            foreach (RunnerNetworkPlayer player in registeredPlayers)
            {
                RunnerSpawnSlot slot = player.OwnerClientId == serverClientId
                    ? RunnerSpawnSlot.Blue
                    : RunnerSpawnSlot.Red;
                player.AssignSpawnSlotServer(slot);
            }
        }

        private void StartRoundServer(int levelIndex)
        {
            LevelCourseDefinition course = courses[levelIndex - 1];
            foreach (RunnerNetworkPlayer player in registeredPlayers)
            {
                player.ResetForRoundServer(course);
            }

            obstacleManager?.ResetForRound(levelIndex);
            obstacleManager?.SetActiveLevel(levelIndex);

            BroadcastRoundStateServer(new RaceRoundState(
                levelIndex,
                RaceRoundPhase.Racing,
                NetworkManager.Singleton.ServerTime.Time,
                RaceRoundState.NoWinner));
        }

        private IEnumerator AdvanceToNextRoundRoutine(int nextLevelIndex)
        {
            yield return new WaitForSeconds(3f);
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                StartRoundServer(nextLevelIndex);
            }
        }

        private IEnumerator ReturnToBootstrapAfterMatchRoutine()
        {
            yield return new WaitForSeconds(4f);
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            }
        }

        private void BroadcastRoundStateServer(RaceRoundState state)
        {
            localRoundState = state;
            foreach (RunnerNetworkPlayer player in registeredPlayers)
            {
                if (player != null)
                {
                    player.SetRoundStateServer(state);
                }
            }

            ApplyObservedRoundState(state);
        }

        private RaceRoundState GetObservedRoundState()
        {
            if (RunnerNetworkPlayer.LocalPlayer != null)
            {
                localRoundState = RunnerNetworkPlayer.LocalPlayer.SharedRoundState;
                return localRoundState;
            }

            foreach (RunnerNetworkPlayer player in registeredPlayers)
            {
                if (player != null)
                {
                    localRoundState = player.SharedRoundState;
                    return localRoundState;
                }
            }

            return localRoundState;
        }

        private void Update()
        {
            ApplyObservedRoundState(GetObservedRoundState());
        }

        private void ApplyObservedRoundState(RaceRoundState current)
        {
            obstacleManager?.SetActiveLevel(current.LevelIndex);
        }
    }
}
