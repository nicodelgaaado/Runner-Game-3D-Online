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
            float hudHeight = Debug.isDebugBuild ? 580f : 160f;
            GUILayout.BeginArea(new Rect(20f, 20f, 320f, hudHeight), GUI.skin.box);
            GUILayout.Label("Online Race");
            GUILayout.Label($"Level: {state.LevelIndex}");
            GUILayout.Label($"Phase: {state.Phase}");
            GUILayout.Label($"Slot: {RunnerNetworkPlayer.LocalPlayer.SpawnSlot}");
            if (SessionRuntime.Session != null)
            {
                GUILayout.Label($"Join Code: {SessionRuntime.Session.Code}");
            }

            if (Debug.isDebugBuild)
            {
                GUILayout.Label($"Local Sequence: {RunnerNetworkPlayer.LocalPlayer.LocalInputSequence}");
                GUILayout.Label($"Received Sequence: {RunnerNetworkPlayer.LocalPlayer.ReceivedInputSequence}");
                GUILayout.Label($"Processed Sequence: {RunnerNetworkPlayer.LocalPlayer.ProcessedInputSequence}");
                GUILayout.Label($"Unacked Gap: {RunnerNetworkPlayer.LocalPlayer.UnackedInputGap}");
                GUILayout.Label($"Pending Inputs: {RunnerNetworkPlayer.LocalPlayer.PendingInputCount}");
                string replayWindow = RunnerNetworkPlayer.LocalPlayer.ReplayWindowClamped
                    ? $"{RunnerNetworkPlayer.LocalPlayer.ReplayWindowSize} (Clamped)"
                    : RunnerNetworkPlayer.LocalPlayer.ReplayWindowSize.ToString();
                GUILayout.Label($"Replay Window: {replayWindow}");
                GUILayout.Label($"Replay Pos Err: {RunnerNetworkPlayer.LocalPlayer.ReplayPositionError:F3}");
                GUILayout.Label($"Replay Path Err: {RunnerNetworkPlayer.LocalPlayer.ReplayPathError:F3}");
                if (!RunnerNetworkPlayer.LocalPlayer.IsServerRole)
                {
                    GUILayout.Label($"Awaiting Snapshot: {RunnerNetworkPlayer.LocalPlayer.AwaitingAuthoritativeSnapshot}");
                }

                GUILayout.Label($"Local Y: {RunnerNetworkPlayer.LocalPlayer.LocalPositionY:F3}");
                string authoritativeY = RunnerNetworkPlayer.LocalPlayer.HasAuthoritativeSnapshot
                    ? RunnerNetworkPlayer.LocalPlayer.AuthoritativePositionY.ToString("F3")
                    : "n/a";
                GUILayout.Label($"Authoritative Y: {authoritativeY}");
                GUILayout.Label($"Support: {RunnerNetworkPlayer.LocalPlayer.GroundSupportStatus}");
                GUILayout.Label($"Support Collider: {RunnerNetworkPlayer.LocalPlayer.GroundSupportColliderName}");
                GUILayout.Label($"Support Normal Y: {RunnerNetworkPlayer.LocalPlayer.GroundSupportNormalY:F3}");
                GUILayout.Label($"IsOwner: {RunnerNetworkPlayer.LocalPlayer.IsOwnerRole}");
                GUILayout.Label($"IsServer: {RunnerNetworkPlayer.LocalPlayer.IsServerRole}");
                GUILayout.Label($"OwnerClientId: {RunnerNetworkPlayer.LocalPlayer.OwnerId}");
                GUILayout.Label($"ServerClientId: {RunnerNetworkPlayer.LocalPlayer.ServerId}");
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
