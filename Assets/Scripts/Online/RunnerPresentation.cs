using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public class RunnerPresentation : MonoBehaviour
    {
        private static readonly string[] SharedAnimatorParameters = { "isMoving", "isFalling", "Climb" };
        private static readonly string[] RedAnimatorParameters = { "RedWin", "RedCry" };
        private static readonly string[] BlueAnimatorParameters = { "BlueWin", "BlueCry" };

        private Animator animator;
        private GameObject activeVisual;
        private RunnerSpawnSlot activeSlot = RunnerSpawnSlot.None;

        public bool HasActiveVisual => activeVisual != null;

        public void ApplyVisuals(RunnerSpawnSlot slot)
        {
            if (slot == RunnerSpawnSlot.None)
            {
                ClearVisuals();
                return;
            }

            if (slot == activeSlot && activeVisual != null)
            {
                return;
            }

            if (activeVisual != null)
            {
                Destroy(activeVisual);
            }

            activeVisual = LegacySceneAdapter.InstantiateVisualPrototype(slot, transform);
            activeSlot = slot;
            animator = FindAnimatorForSlot(activeVisual, slot);
        }

        public void UpdateAnimationState(RunnerSpawnSlot slot, bool moving, bool falling, bool climbing, bool winner, bool loser)
        {
            if (!IsGameplaySceneLoaded())
            {
                ClearVisuals();
                return;
            }

            if (slot == RunnerSpawnSlot.None)
            {
                ClearVisuals();
                return;
            }

            if (animator == null)
            {
                ApplyVisuals(slot);
            }

            if (animator == null)
            {
                return;
            }

            SetBoolIfExists(animator, "isMoving", moving);
            SetBoolIfExists(animator, "isFalling", falling);
            SetBoolIfExists(animator, "Climb", climbing);

            if (slot == RunnerSpawnSlot.Red)
            {
                SetBoolIfExists(animator, "RedWin", winner);
                SetBoolIfExists(animator, "RedCry", loser);
            }
            else if (slot == RunnerSpawnSlot.Blue)
            {
                SetBoolIfExists(animator, "BlueWin", winner);
                SetBoolIfExists(animator, "BlueCry", loser);
            }
        }

        private void ClearVisuals()
        {
            if (activeVisual != null)
            {
                Destroy(activeVisual);
                activeVisual = null;
            }

            animator = null;
            activeSlot = RunnerSpawnSlot.None;
        }

        private static bool IsGameplaySceneLoaded()
        {
            Scene gameplayScene = SceneManager.GetSceneByName("Joc");
            return (gameplayScene.IsValid() && gameplayScene.isLoaded) || SceneManager.GetActiveScene().name == "Joc";
        }

        private static Animator FindAnimatorForSlot(GameObject visualRoot, RunnerSpawnSlot slot)
        {
            if (visualRoot == null)
            {
                return null;
            }

            string[] requiredParameters = slot == RunnerSpawnSlot.Blue ? BlueAnimatorParameters : RedAnimatorParameters;
            Animator fallbackAnimator = null;

            foreach (Animator candidate in visualRoot.GetComponentsInChildren<Animator>(true))
            {
                if (candidate == null)
                {
                    continue;
                }

                fallbackAnimator ??= candidate;
                if (HasParameters(candidate, SharedAnimatorParameters) && HasParameters(candidate, requiredParameters))
                {
                    return candidate;
                }
            }

            return fallbackAnimator;
        }

        private static bool HasParameters(Animator targetAnimator, string[] parameterNames)
        {
            foreach (string parameterName in parameterNames)
            {
                if (!HasParameter(targetAnimator, parameterName))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasParameter(Animator targetAnimator, string parameterName)
        {
            foreach (AnimatorControllerParameter parameter in targetAnimator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == parameterName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetBoolIfExists(Animator targetAnimator, string parameterName, bool value)
        {
            if (HasParameter(targetAnimator, parameterName))
            {
                targetAnimator.SetBool(parameterName, value);
            }
        }
    }
}
