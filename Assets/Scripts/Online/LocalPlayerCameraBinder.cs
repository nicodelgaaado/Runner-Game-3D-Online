using Cinemachine;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public class LocalPlayerCameraBinder : MonoBehaviour
    {
        [SerializeField] private float followDistance = 24f;
        [SerializeField] private float followHeight = 18f;
        [SerializeField] private float smoothing = 8f;

        private Camera fallbackCamera;
        private Transform followTarget;
        private RunnerSpawnSlot currentSlot = RunnerSpawnSlot.None;
        private static readonly Dictionary<string, Rect> AuthoredViewportRects = new();
        private bool bindingDirty = true;
        private bool usingFallbackCamera;
        private bool missingRigLogged;

        public bool HasActiveCameraBinding
        {
            get
            {
                if (usingFallbackCamera)
                {
                    return fallbackCamera != null && fallbackCamera.enabled;
                }

                if (currentSlot == RunnerSpawnSlot.None)
                {
                    return false;
                }

                CameraRig rig = ResolveRig(currentSlot);
                return rig.MainCamera != null
                    && rig.MainCamera.enabled
                    && rig.VirtualCamera != null
                    && rig.VirtualCamera.enabled;
            }
        }

        private struct CameraRig
        {
            public string MainCameraName;
            public string VirtualCameraName;
            public Camera MainCamera;
            public Rect DefaultRect;
            public AudioListener AudioListener;
            public CinemachineBrain Brain;
            public CinemachineVirtualCamera VirtualCamera;
        }

        public void Bind(Transform target, RunnerSpawnSlot slot)
        {
            followTarget = target;
            if (currentSlot != slot)
            {
                currentSlot = slot;
                missingRigLogged = false;
            }

            bindingDirty = true;
        }

        public void Release()
        {
            DisableAllLegacyRigs();
            DisposeFallbackCamera();
            bindingDirty = true;
            usingFallbackCamera = false;
        }

        private void LateUpdate()
        {
            if (followTarget == null)
            {
                return;
            }

            if (!IsGameplaySceneLoaded())
            {
                Release();
                return;
            }

            if (bindingDirty || (usingFallbackCamera && currentSlot != RunnerSpawnSlot.None))
            {
                TryBindCamera();
            }

            if (usingFallbackCamera)
            {
                UpdateFallbackCamera();
            }
        }

        private void TryBindCamera()
        {
            bindingDirty = false;

            if (currentSlot == RunnerSpawnSlot.None)
            {
                DisableAllLegacyRigs();
                DisposeFallbackCamera();
                usingFallbackCamera = false;
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (OnlineSceneRuntime.ShouldUseLegacyGameplayCameraRig(activeScene) && TryActivateLegacyRig(currentSlot))
            {
                DisposeFallbackCamera();
                usingFallbackCamera = false;
                return;
            }

            DisableAllLegacyRigs();
            EnsureFallbackCamera();
            usingFallbackCamera = true;

            if (OnlineSceneRuntime.ShouldUseLegacyGameplayCameraRig(activeScene) && !missingRigLogged)
            {
                Debug.LogError($"Failed to bind legacy camera rig for slot '{currentSlot}'. Falling back to OnlineLocalCamera.");
                missingRigLogged = true;
            }
        }

        private bool TryActivateLegacyRig(RunnerSpawnSlot slot)
        {
            CameraRig selectedRig = ResolveRig(slot);
            if (selectedRig.MainCamera == null || selectedRig.VirtualCamera == null)
            {
                return false;
            }

            DisableAllLegacyRigs();
            EnableRig(selectedRig);

            selectedRig.VirtualCamera.Follow = followTarget;
            selectedRig.VirtualCamera.LookAt = followTarget;
            selectedRig.MainCamera.rect = new Rect(0f, 0f, 1f, 1f);
            return true;
        }

        private static void DisableAllLegacyRigs()
        {
            DisableRig(ResolveRig(RunnerSpawnSlot.Red));
            DisableRig(ResolveRig(RunnerSpawnSlot.Blue));
        }

        private static void EnableRig(CameraRig rig)
        {
            if (rig.MainCamera != null)
            {
                rig.MainCamera.gameObject.SetActive(true);
                rig.MainCamera.enabled = true;
            }

            if (rig.AudioListener != null)
            {
                rig.AudioListener.enabled = true;
            }

            if (rig.Brain != null)
            {
                rig.Brain.enabled = true;
            }

            if (rig.VirtualCamera != null)
            {
                rig.VirtualCamera.gameObject.SetActive(true);
                rig.VirtualCamera.enabled = true;
            }
        }

        private static void DisableRig(CameraRig rig)
        {
            if (rig.VirtualCamera != null)
            {
                rig.VirtualCamera.enabled = false;
                rig.VirtualCamera.gameObject.SetActive(false);
            }

            if (rig.AudioListener != null)
            {
                rig.AudioListener.enabled = false;
            }

            if (rig.Brain != null)
            {
                rig.Brain.enabled = false;
            }

            if (rig.MainCamera != null)
            {
                rig.MainCamera.rect = rig.DefaultRect;
                rig.MainCamera.enabled = false;
                rig.MainCamera.gameObject.SetActive(false);
            }
        }

        private static CameraRig ResolveRig(RunnerSpawnSlot slot)
        {
            string mainCameraName = slot == RunnerSpawnSlot.Blue ? "MainBlueCamera" : "MainRedCamera";
            string virtualCameraName = slot == RunnerSpawnSlot.Blue ? "BlueCamera" : "RedCamera";

            Camera mainCamera = FindComponentByName<Camera>(mainCameraName);
            Rect defaultRect = default;
            if (mainCamera != null)
            {
                if (!AuthoredViewportRects.TryGetValue(mainCameraName, out defaultRect))
                {
                    defaultRect = mainCamera.rect;
                    AuthoredViewportRects[mainCameraName] = defaultRect;
                }
            }

            return new CameraRig
            {
                MainCameraName = mainCameraName,
                VirtualCameraName = virtualCameraName,
                MainCamera = mainCamera,
                DefaultRect = defaultRect,
                AudioListener = mainCamera != null ? mainCamera.GetComponent<AudioListener>() : null,
                Brain = mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null,
                VirtualCamera = FindComponentByName<CinemachineVirtualCamera>(virtualCameraName)
            };
        }

        private static T FindComponentByName<T>(string objectName) where T : Component
        {
            foreach (T component in Object.FindObjectsByType<T>(FindObjectsInactive.Include))
            {
                if (component != null && component.gameObject.name == objectName)
                {
                    return component;
                }
            }

            return null;
        }

        private void UpdateFallbackCamera()
        {
            if (fallbackCamera == null)
            {
                return;
            }

            Vector3 desiredPosition = followTarget.position - (followTarget.forward * followDistance) + (Vector3.up * followHeight);
            Transform cameraTransform = fallbackCamera.transform;
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, desiredPosition, Time.deltaTime * smoothing);

            Quaternion targetRotation = Quaternion.LookRotation(followTarget.position + (Vector3.up * 6f) - cameraTransform.position, Vector3.up);
            cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, targetRotation, Time.deltaTime * smoothing);
        }

        private void EnsureFallbackCamera()
        {
            if (fallbackCamera != null || !IsGameplaySceneLoaded())
            {
                return;
            }

            GameObject cameraObject = new GameObject("OnlineLocalCamera");
            fallbackCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            fallbackCamera.tag = "MainCamera";
            fallbackCamera.depth = 10f;

            Vector3 desiredPosition = followTarget.position - (followTarget.forward * followDistance) + (Vector3.up * followHeight);
            cameraObject.transform.SetPositionAndRotation(
                desiredPosition,
                Quaternion.LookRotation(followTarget.position + (Vector3.up * 6f) - desiredPosition, Vector3.up));
        }

        private void DisposeFallbackCamera()
        {
            if (fallbackCamera == null)
            {
                return;
            }

            Destroy(fallbackCamera.gameObject);
            fallbackCamera = null;
        }

        private static bool IsGameplaySceneLoaded()
        {
            Scene gameplayScene = SceneManager.GetSceneByName("Joc");
            return (gameplayScene.IsValid() && gameplayScene.isLoaded) || SceneManager.GetActiveScene().name == "Joc";
        }
    }
}
