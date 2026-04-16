using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunnerGame.Online
{
    public class LocalMatchHudController : MonoBehaviour
    {
        private bool pauseOverlayVisible;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                pauseOverlayVisible = !pauseOverlayVisible;
            }
        }

        private void OnGUI()
        {
            if (NetworkRaceManager.Instance == null || RunnerNetworkPlayer.LocalPlayer == null)
            {
                return;
            }

            RaceRoundState state = NetworkRaceManager.Instance.RoundState;
            NetworkRunner runner = SessionRuntime.Runner;
            float hudHeight = Debug.isDebugBuild ? 340f : 160f;
            GUILayout.BeginArea(new Rect(20f, 20f, 340f, hudHeight), GUI.skin.box);
            GUILayout.Label("Online Race");
            GUILayout.Label($"Level: {state.LevelIndex}");
            GUILayout.Label($"Phase: {state.Phase}");
            GUILayout.Label($"Slot: {RunnerNetworkPlayer.LocalPlayer.SpawnSlot}");
            GUILayout.Label($"Room Code: {SessionRuntime.SessionCode}");

            if (Debug.isDebugBuild && runner != null)
            {
                GUILayout.Label($"Local PlayerRef: {runner.LocalPlayer}");
                GUILayout.Label($"Owner PlayerRef: {RunnerNetworkPlayer.LocalPlayer.OwnerPlayer}");
                GUILayout.Label($"Has State Authority: {RunnerNetworkPlayer.LocalPlayer.HasStateAuthority}");
                GUILayout.Label($"Has Input Authority: {RunnerNetworkPlayer.LocalPlayer.HasInputAuthority}");
                GUILayout.Label($"Is Scene Authority: {runner.IsSceneAuthority}");
                GUILayout.Label($"Is Master Client: {runner.IsSharedModeMasterClient}");
                GUILayout.Label($"Runner State: {runner.State}");
                GUILayout.Label($"Session Name: {runner.SessionInfo.Name}");
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
                SessionBootstrapper.Instance.LeaveSession();
            }

            if (GUILayout.Button("Close"))
            {
                pauseOverlayVisible = false;
            }

            GUILayout.EndArea();
        }
    }
}
