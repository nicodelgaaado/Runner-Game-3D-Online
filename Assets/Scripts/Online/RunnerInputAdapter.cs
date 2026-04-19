using UnityEngine;
using UnityEngine.InputSystem;

namespace RunnerGame.Online
{
    public class RunnerInputAdapter : MonoBehaviour
    {
        public RunnerInputState Capture()
        {
            return CaptureCurrentDevices();
        }

        public static RunnerInputState CaptureCurrentDevices()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;

            bool keyboardMove = keyboard != null && (
                keyboard.wKey.isPressed ||
                keyboard.upArrowKey.isPressed ||
                keyboard.spaceKey.isPressed);

            bool gamepadMove = gamepad != null && (
                gamepad.buttonSouth.isPressed ||
                gamepad.leftStick.up.isPressed);

            return new RunnerInputState(keyboardMove || gamepadMove);
        }
    }
}
