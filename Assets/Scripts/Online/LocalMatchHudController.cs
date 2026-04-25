using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public class LocalMatchHudController : MonoBehaviour
    {
        private bool pauseOverlayVisible;
        private bool showGameplayDebug;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (Debug.isDebugBuild && keyboard != null && keyboard.f3Key.wasPressedThisFrame)
            {
                showGameplayDebug = !showGameplayDebug;
            }

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                pauseOverlayVisible = !pauseOverlayVisible;
                OnlineAudioDirector.Instance?.PlayUiSelect();
                OnlineAudioDirector.Instance?.SetGameplayMusicPaused(pauseOverlayVisible);
            }
        }

        private void OnDisable()
        {
            if (!pauseOverlayVisible)
            {
                return;
            }

            pauseOverlayVisible = false;
            OnlineAudioDirector.Instance?.SetGameplayMusicPaused(false);
        }

        private void OnGUI()
        {
            if (NetworkRaceManager.Instance == null || RunnerNetworkPlayer.LocalPlayer == null)
            {
                return;
            }

            RaceRoundState state = NetworkRaceManager.Instance.RoundState;
            NetworkRunner runner = SessionRuntime.Runner;
            bool showDebugPanel = Debug.isDebugBuild && showGameplayDebug && runner != null;
            float hudHeight = showDebugPanel ? 490f : 160f;
            GUILayout.BeginArea(new Rect(20f, 20f, 340f, hudHeight), GUI.skin.box);
            GUILayout.Label("Online Race");
            GUILayout.Label($"Level: {state.LevelIndex}");
            GUILayout.Label($"Phase: {state.Phase}");
            GUILayout.Label($"Slot: {RunnerNetworkPlayer.LocalPlayer.SpawnSlot}");
            GUILayout.Label($"Room Code: {SessionRuntime.SessionCode}");

            if (showDebugPanel)
            {
                string sessionName = runner.SessionInfo.IsValid && !string.IsNullOrWhiteSpace(runner.SessionInfo.Name)
                    ? runner.SessionInfo.Name
                    : SessionRuntime.SessionCode;

                GUILayout.Label($"Local PlayerRef: {runner.LocalPlayer}");
                GUILayout.Label($"Owner PlayerRef: {RunnerNetworkPlayer.LocalPlayer.OwnerPlayer}");
                GUILayout.Label($"Replicated Owner PlayerRef: {RunnerNetworkPlayer.LocalPlayer.ReplicatedOwnerPlayerRef}");
                GUILayout.Label($"Replicated Slot: {RunnerNetworkPlayer.LocalPlayer.ReplicatedSpawnSlotValue}");
                GUILayout.Label($"Input Authority PlayerRef: {RunnerNetworkPlayer.LocalPlayer.InputAuthorityPlayer}");
                GUILayout.Label($"State Authority PlayerRef: {RunnerNetworkPlayer.LocalPlayer.StateAuthorityPlayer}");
                GUILayout.Label($"Has State Authority: {RunnerNetworkPlayer.LocalPlayer.HasStateAuthority}");
                GUILayout.Label($"Has Input Authority: {RunnerNetworkPlayer.LocalPlayer.HasInputAuthority}");
                GUILayout.Label($"Is Scene Authority: {runner.IsSceneAuthority}");
                GUILayout.Label($"Is Master Client: {runner.IsSharedModeMasterClient}");
                GUILayout.Label($"Drives Scene Load: {runner.IsSharedModeMasterClient || runner.IsSceneAuthority}");
                GUILayout.Label($"Runner State: {runner.State}");
                GUILayout.Label($"Session Name: {sessionName}");
                GUILayout.Label($"Active Scene: {SceneManager.GetActiveScene().name}");
                GUILayout.Label($"Fusion Scene Info: {GetFusionSceneInfo(runner)}");
                GUILayout.Label($"Respawning: {RunnerNetworkPlayer.LocalPlayer.IsRespawning}");
                GUILayout.Label($"Path State: {RunnerNetworkPlayer.LocalPlayer.ReplicatedPathState:F2}");
                GUILayout.Label($"Local Y: {RunnerNetworkPlayer.LocalPlayer.LocalPositionY:F3}");
                GUILayout.Label($"Support: {RunnerNetworkPlayer.LocalPlayer.GroundSupportStatus}");
                GUILayout.Label($"Support Collider: {RunnerNetworkPlayer.LocalPlayer.GroundSupportColliderName}");
                GUILayout.Label($"Support Normal Y: {RunnerNetworkPlayer.LocalPlayer.GroundSupportNormalY:F3}");
            }

            GUILayout.EndArea();

            if (!pauseOverlayVisible)
            {
                return;
            }

            Rect modalRect = new Rect((Screen.width * 0.5f) - 160f, (Screen.height * 0.5f) - 90f, 320f, 180f);
            GUILayout.BeginArea(modalRect, GUI.skin.window);
            GUILayout.Label("Local Pause");
            GUILayout.Label("This overlay does not pause the networked race.");

            if (GUILayout.Button("Return To Menu") && SessionBootstrapper.Instance != null)
            {
                OnlineAudioDirector.Instance?.PlayUiSelect();
                OnlineAudioDirector.Instance?.SetGameplayMusicPaused(false);
                SessionBootstrapper.Instance.LeaveSession();
            }

            if (GUILayout.Button("Close"))
            {
                OnlineAudioDirector.Instance?.PlayUiSelect();
                pauseOverlayVisible = false;
                OnlineAudioDirector.Instance?.SetGameplayMusicPaused(false);
            }

            GUILayout.EndArea();
        }

        private static string GetFusionSceneInfo(NetworkRunner runner)
        {
            return runner != null && runner.TryGetSceneInfo(out NetworkSceneInfo sceneInfo)
                ? sceneInfo.ToString()
                : "invalid";
        }
    }
}
