using System.Collections.Generic;
using Cinemachine;
using PathCreation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public static class LegacySceneAdapter
    {
        private const string OnlineDirectionalLightName = "OnlineDirectionalLight";
        private const string BootstrapDirectionalLightName = "Directional Light";
        private const string BootstrapDirectionalLightTemplateName = "BootstrapDirectionalLightTemplate";
        private const string GameplaySceneName = "Joc";
        private const string MenuCameraName = "MenuCamera";
        private const string CreditsCameraName = "CreditsCamera";
        private const string MainBlueCameraName = "MainBlueCamera";
        private const string MainRedCameraName = "MainRedCamera";
        private const string BlueVirtualCameraName = "BlueCamera";
        private const string RedVirtualCameraName = "RedCamera";

        private static GameObject redPrototype;
        private static GameObject bluePrototype;
        private static ObstacleManager obstacleManager;
        private static GameObject bootstrapDirectionalLightTemplate;
        private static GameObject onlineDirectionalLightInstance;

        public static ObstacleManager ObstacleManager => obstacleManager;

        public static void HandleSceneLoaded(Scene scene)
        {
            if (scene.name == "Bootstrap")
            {
                CaptureBootstrapDirectionalLightTemplate(scene);
                DestroyOnlineDirectionalLight();
                RefreshSceneLightingEnvironment();
                return;
            }

            if (scene.name == "Joc" && SessionRuntime.Runner != null && SessionRuntime.Runner.IsRunning)
            {
                SuppressLegacyLocalRuntime(scene);
            }
        }

        public static void InitializeForOnlineScene()
        {
            EnsureGameplayLighting();
            RefreshSceneLightingEnvironment();
            SuppressLegacyLocalRuntime(SceneManager.GetSceneByName(GameplaySceneName));

            RedPlayerMovement redPlayer = Object.FindAnyObjectByType<RedPlayerMovement>(FindObjectsInactive.Include);
            BluePlayerMovement bluePlayer = Object.FindAnyObjectByType<BluePlayerMovement>(FindObjectsInactive.Include);
            obstacleManager = Object.FindAnyObjectByType<ObstacleManager>(FindObjectsInactive.Include);

            GlobalVolumeManager volumeManager = Object.FindAnyObjectByType<GlobalVolumeManager>(FindObjectsInactive.Include);
            if (volumeManager != null)
            {
                volumeManager.Color();
                volumeManager.clearBlur();
            }

            if (redPlayer != null)
            {
                redPrototype = redPlayer.gameObject;
                redPlayer.enabled = false;
                redPrototype.SetActive(false);
            }

            if (bluePlayer != null)
            {
                bluePrototype = bluePlayer.gameObject;
                bluePlayer.enabled = false;
                bluePrototype.SetActive(false);
            }

            DisableLegacyCameraRig(SceneManager.GetSceneByName(GameplaySceneName), MainBlueCameraName, BlueVirtualCameraName);
            DisableLegacyCameraRig(SceneManager.GetSceneByName(GameplaySceneName), MainRedCameraName, RedVirtualCameraName);
        }

        public static IReadOnlyList<LevelCourseDefinition> BuildCourses()
        {
            List<LevelCourseDefinition> courses = new List<LevelCourseDefinition>(5);
            PathCreator[] creators = GetLegacyPathCreators();

            if (creators.Length < 5)
            {
                return courses;
            }

            courses.Add(new LevelCourseDefinition { LevelIndex = 1, PathCreator = creators[0], FinishDistance = 480f, StartFacingEuler = new Vector3(0f, 90f, 0f) });
            courses.Add(new LevelCourseDefinition { LevelIndex = 2, PathCreator = creators[1], FinishDistance = 309f, StartFacingEuler = Vector3.zero });
            courses.Add(new LevelCourseDefinition { LevelIndex = 3, PathCreator = creators[2], FinishDistance = 372f, StartFacingEuler = Vector3.zero });
            courses.Add(new LevelCourseDefinition
            {
                LevelIndex = 4,
                PathCreator = creators[3],
                FinishDistance = 387f,
                StartFacingEuler = new Vector3(0f, 180f, 0f),
                HasClimbSegment = true,
                ClimbStartDistance = 286f
            });
            courses.Add(new LevelCourseDefinition { LevelIndex = 5, PathCreator = creators[4], FinishDistance = 632f, StartFacingEuler = new Vector3(0f, 90f, 0f) });
            return courses;
        }

        public static GameObject InstantiateVisualPrototype(RunnerSpawnSlot slot, Transform parent)
        {
            GameObject source = slot == RunnerSpawnSlot.Blue ? bluePrototype : redPrototype;
            if (source == null)
            {
                return null;
            }

            GameObject clone = Object.Instantiate(source, parent, false);
            clone.name = $"{slot}Visual";
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            clone.SetActive(true);
            SanitizeVisualClone(clone, slot);
            return clone;
        }

        private static PathCreator[] GetLegacyPathCreators()
        {
            List<PathCreator> creators = new List<PathCreator>();
            RedPlayerMovement redPlayer = Object.FindAnyObjectByType<RedPlayerMovement>(FindObjectsInactive.Include);
            BluePlayerMovement bluePlayer = Object.FindAnyObjectByType<BluePlayerMovement>(FindObjectsInactive.Include);

            if (redPlayer != null)
            {
                AddDistinct(creators, redPlayer.pathCreator1);
                AddDistinct(creators, redPlayer.pathCreator2);
                AddDistinct(creators, redPlayer.pathCreator3);
                AddDistinct(creators, redPlayer.pathCreator4);
                AddDistinct(creators, redPlayer.pathCreator5);
            }
            else if (bluePlayer != null)
            {
                AddDistinct(creators, bluePlayer.pathCreator1);
                AddDistinct(creators, bluePlayer.pathCreator2);
                AddDistinct(creators, bluePlayer.pathCreator3);
                AddDistinct(creators, bluePlayer.pathCreator4);
                AddDistinct(creators, bluePlayer.pathCreator5);
            }

            return creators.ToArray();
        }

        private static void AddDistinct(List<PathCreator> creators, PathCreator candidate)
        {
            if (candidate != null && !creators.Contains(candidate))
            {
                creators.Add(candidate);
            }
        }

        private static void EnsureGameplayLighting()
        {
            if (TryGetGameplayScene(out Scene gameplayScene))
            {
                if (TryBindDirectionalLightFromScene(gameplayScene, activeOnly: true, activateIfNeeded: false))
                {
                    DestroyOnlineDirectionalLight();
                    return;
                }

                if (TryBindDirectionalLightFromScene(gameplayScene, activeOnly: false, activateIfNeeded: true))
                {
                    DestroyOnlineDirectionalLight();
                    return;
                }
            }

            if (TryBindActiveDirectionalLightFromLoadedScenes())
            {
                return;
            }

            if (onlineDirectionalLightInstance == null && bootstrapDirectionalLightTemplate != null)
            {
                onlineDirectionalLightInstance = Object.Instantiate(bootstrapDirectionalLightTemplate);
                onlineDirectionalLightInstance.name = OnlineDirectionalLightName;
                onlineDirectionalLightInstance.SetActive(true);
                Object.DontDestroyOnLoad(onlineDirectionalLightInstance);
            }

            if (onlineDirectionalLightInstance == null)
            {
                Debug.LogWarning("Online scene lighting could not be restored because no Bootstrap directional light template was captured.");
                return;
            }

            onlineDirectionalLightInstance.SetActive(true);
            Light onlineLight = onlineDirectionalLightInstance.GetComponent<Light>();
            if (onlineLight != null)
            {
                onlineLight.enabled = true;
                RenderSettings.sun = onlineLight;
            }
        }

        public static void SuppressLegacyLocalRuntime(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            canvasManager legacyCanvas = FindComponentInScene<canvasManager>(scene);
            if (legacyCanvas != null)
            {
                legacyCanvas.enabled = false;

                DisableCanvas(legacyCanvas.Menu);
                DisableCanvas(legacyCanvas.Pause);
                DisableCanvas(legacyCanvas.Credits);
                DisableCanvas(legacyCanvas.Instructions);

                DisableCanvasGroupRoot(legacyCanvas.BlackBlueCanvasGroup);
                DisableCanvasGroupRoot(legacyCanvas.BlackRedCanvasGroup);
                DisableCanvasGroupRoot(legacyCanvas.LevelText1);
                DisableCanvasGroupRoot(legacyCanvas.LevelText2);
                DisableCanvasGroupRoot(legacyCanvas.LevelText3);
                DisableCanvasGroupRoot(legacyCanvas.LevelText4);
                DisableCanvasGroupRoot(legacyCanvas.LevelText5);

                DisableCameraObject(legacyCanvas.cameraMenu);
                DisableCameraObject(legacyCanvas.CreditsCamera);
                legacyCanvas.gameObject.SetActive(false);
            }

            RedPlayerMovement redPlayer = FindComponentInScene<RedPlayerMovement>(scene);
            if (redPlayer != null)
            {
                redPlayer.enabled = false;
                redPlayer.gameObject.SetActive(false);
            }

            BluePlayerMovement bluePlayer = FindComponentInScene<BluePlayerMovement>(scene);
            if (bluePlayer != null)
            {
                bluePlayer.enabled = false;
                bluePlayer.gameObject.SetActive(false);
            }

            EventSystem eventSystem = FindComponentInScene<EventSystem>(scene);
            if (eventSystem != null)
            {
                eventSystem.gameObject.SetActive(false);
            }

            DisableLegacyCameraRig(scene, MainBlueCameraName, BlueVirtualCameraName);
            DisableLegacyCameraRig(scene, MainRedCameraName, RedVirtualCameraName);
            DisableRootObject(scene, MenuCameraName);
            DisableRootObject(scene, CreditsCameraName);
        }

        private static void RefreshSceneLightingEnvironment()
        {
            if (TryGetGameplayScene(out Scene gameplayScene)
                && TryBindDirectionalLightFromScene(gameplayScene, activeOnly: true, activateIfNeeded: false))
            {
                DynamicGI.UpdateEnvironment();
                return;
            }

            TryBindActiveDirectionalLightFromLoadedScenes();
            DynamicGI.UpdateEnvironment();
        }

        private static bool TryGetGameplayScene(out Scene scene)
        {
            scene = SceneManager.GetSceneByName(GameplaySceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        private static bool TryBindDirectionalLightFromScene(Scene scene, bool activeOnly, bool activateIfNeeded)
        {
            Light sceneDirectionalLight = FindDirectionalLight(scene, activeOnly);
            if (sceneDirectionalLight == null)
            {
                return false;
            }

            if (activateIfNeeded && !sceneDirectionalLight.gameObject.activeSelf)
            {
                sceneDirectionalLight.gameObject.SetActive(true);
            }

            sceneDirectionalLight.enabled = true;
            RenderSettings.sun = sceneDirectionalLight;
            return true;
        }

        private static bool TryBindActiveDirectionalLightFromLoadedScenes()
        {
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light == null || light.type != LightType.Directional || !light.gameObject.activeInHierarchy)
                {
                    continue;
                }

                light.enabled = true;
                RenderSettings.sun = light;
                return true;
            }

            return false;
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject == null)
                {
                    continue;
                }

                T component = rootObject.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static T FindComponentInSceneByName<T>(Scene scene, string objectName) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject == null)
                {
                    continue;
                }

                foreach (T component in rootObject.GetComponentsInChildren<T>(true))
                {
                    if (component != null && component.gameObject.name == objectName)
                    {
                        return component;
                    }
                }
            }

            return null;
        }

        private static void DisableCanvas(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.enabled = false;
            canvas.gameObject.SetActive(false);
        }

        private static void DisableCanvasGroupRoot(CanvasGroup canvasGroup)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.gameObject.SetActive(false);
        }

        private static void DisableCameraObject(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            camera.enabled = false;
            camera.gameObject.SetActive(false);
        }

        private static void DisableRootObject(Scene scene, string objectName)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(objectName))
            {
                return;
            }

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject != null && rootObject.name == objectName)
                {
                    rootObject.SetActive(false);
                    return;
                }
            }
        }

        private static void DisableLegacyCameraRig(Scene scene, string mainCameraName, string virtualCameraName)
        {
            Camera mainCamera = FindComponentInSceneByName<Camera>(scene, mainCameraName);
            if (mainCamera != null)
            {
                AudioListener audioListener = mainCamera.GetComponent<AudioListener>();
                if (audioListener != null)
                {
                    audioListener.enabled = false;
                }

                CinemachineBrain brain = mainCamera.GetComponent<CinemachineBrain>();
                if (brain != null)
                {
                    brain.enabled = false;
                }

                mainCamera.enabled = false;
                mainCamera.gameObject.SetActive(false);
            }

            CinemachineVirtualCamera virtualCamera = FindComponentInSceneByName<CinemachineVirtualCamera>(scene, virtualCameraName);
            if (virtualCamera != null)
            {
                virtualCamera.enabled = false;
                virtualCamera.gameObject.SetActive(false);
            }
        }

        private static void CaptureBootstrapDirectionalLightTemplate(Scene scene)
        {
            GameObject sceneDirectionalLight = FindDirectionalLightObject(scene);
            if (sceneDirectionalLight == null)
            {
                return;
            }

            if (bootstrapDirectionalLightTemplate != null)
            {
                Object.Destroy(bootstrapDirectionalLightTemplate);
            }

            bootstrapDirectionalLightTemplate = Object.Instantiate(sceneDirectionalLight);
            bootstrapDirectionalLightTemplate.name = BootstrapDirectionalLightTemplateName;
            bootstrapDirectionalLightTemplate.SetActive(false);
            Object.DontDestroyOnLoad(bootstrapDirectionalLightTemplate);

            Light bootstrapLight = sceneDirectionalLight.GetComponent<Light>();
            if (bootstrapLight != null)
            {
                bootstrapLight.enabled = true;
                RenderSettings.sun = bootstrapLight;
            }
        }

        private static Light FindDirectionalLight(Scene scene, bool activeOnly)
        {
            GameObject directionalLightObject = FindDirectionalLightObject(scene, activeOnly);
            return directionalLightObject != null ? directionalLightObject.GetComponent<Light>() : null;
        }

        private static GameObject FindDirectionalLightObject(Scene scene)
        {
            return FindDirectionalLightObject(scene, activeOnly: false);
        }

        private static GameObject FindDirectionalLightObject(Scene scene, bool activeOnly)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject == null)
                {
                    continue;
                }

                if (rootObject.name == BootstrapDirectionalLightName)
                {
                    Light namedLight = rootObject.GetComponent<Light>();
                    if (IsDirectionalLightEligible(namedLight, activeOnly))
                    {
                        return rootObject;
                    }
                }

                foreach (Light light in rootObject.GetComponentsInChildren<Light>(true))
                {
                    if (IsDirectionalLightEligible(light, activeOnly))
                    {
                        return light.gameObject;
                    }
                }
            }

            return null;
        }

        private static bool IsDirectionalLightEligible(Light light, bool activeOnly)
        {
            return light != null
                && light.type == LightType.Directional
                && (!activeOnly || light.gameObject.activeInHierarchy);
        }

        private static void DestroyOnlineDirectionalLight()
        {
            if (onlineDirectionalLightInstance == null)
            {
                return;
            }

            onlineDirectionalLightInstance.SetActive(false);
            Object.Destroy(onlineDirectionalLightInstance);
            onlineDirectionalLightInstance = null;
        }

        private static void SanitizeVisualClone(GameObject clone, RunnerSpawnSlot slot)
        {
            foreach (Rigidbody rigidbody in clone.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.Destroy(rigidbody);
            }

            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
            {
                Object.Destroy(collider);
            }

            foreach (AudioSource audioSource in clone.GetComponentsInChildren<AudioSource>(true))
            {
                Object.Destroy(audioSource);
            }

            foreach (AudioListener listener in clone.GetComponentsInChildren<AudioListener>(true))
            {
                Object.Destroy(listener);
            }

            foreach (Camera camera in clone.GetComponentsInChildren<Camera>(true))
            {
                Object.Destroy(camera.gameObject);
            }

            foreach (Light light in clone.GetComponentsInChildren<Light>(true))
            {
                Object.Destroy(light.gameObject);
            }

            RedPlayerMovement redMovement = clone.GetComponent<RedPlayerMovement>();
            if (redMovement != null)
            {
                Object.Destroy(redMovement);
            }

            BluePlayerMovement blueMovement = clone.GetComponent<BluePlayerMovement>();
            if (blueMovement != null)
            {
                Object.Destroy(blueMovement);
            }
        }
    }
}
