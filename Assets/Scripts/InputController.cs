using UnityEngine;
using UnityEngine.InputSystem;

public class InputController : MonoBehaviour
{
    public Vector2 MoveInput() {
      Vector2 moveInput = Vector2.zero;
      Keyboard keyboard = Keyboard.current;
      if (keyboard != null)
      {
          if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
          {
              moveInput.x -= 1f;
          }

          if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
          {
              moveInput.x += 1f;
          }

          if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
          {
              moveInput.y -= 1f;
          }

          if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
          {
              moveInput.y += 1f;
          }
      }

      Gamepad gamepad = Gamepad.current;
      if (gamepad != null)
      {
          moveInput += gamepad.leftStick.ReadValue();
      }

      return Vector2.ClampMagnitude(moveInput, 1f);
    }
}
