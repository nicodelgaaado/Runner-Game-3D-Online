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
        private static readonly string[] MenuCameraNames =
        {
            "MenuCamera",
            "CreditsCamera"
        };

        private const string OnlineDirectionalLightName = "OnlineDirectionalLight";
        private const string BootstrapDirectionalLightName = "Directional Light";
        private const string BootstrapDirectionalLightTemplateName = "BootstrapDirectionalLightTemplate";

        private static GameObject redPrototype;
        private static GameObject bluePrototype;
        private static ObstacleManager obstacleManager;
        private static GameObject bootstrapDirectionalLightTemplate;
        private static GameObject onlineDirectionalLightInstance;

        public static ObstacleManager ObstacleManager => obstacleManager;

        public static void HandleSceneLoaded(Scene scene)
        {
            if (scene.name != "Bootstrap")
            {
                return;
            }

            CaptureBootstrapDirectionalLightTemplate(scene);
            DestroyOnlineDirectionalLight();
            RefreshSceneLightingEnvironment();
        }

        public static void InitializeForOnlineScene()
        {
            EnsureGameplayLighting();
            RefreshSceneLightingEnvironment();

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

            canvasManager legacyCanvas = Object.FindAnyObjectByType<canvasManager>(FindObjectsInactive.Include);
            if (legacyCanvas != null)
            {
                legacyCanvas.enabled = false;

                if (legacyCanvas.Menu != null)
                {
                    legacyCanvas.Menu.gameObject.SetActive(false);
                }

                if (legacyCanvas.Pause != null)
                {
                    legacyCanvas.Pause.gameObject.SetActive(false);
                }

                if (legacyCanvas.Credits != null)
                {
                    legacyCanvas.Credits.gameObject.SetActive(false);
                }

                if (legacyCanvas.Instructions != null)
                {
                    legacyCanvas.Instructions.gameObject.SetActive(false);
                }

                if (legacyCanvas.cameraMenu != null)
                {
                    legacyCanvas.cameraMenu.gameObject.SetActive(false);
                }

                if (legacyCanvas.CreditsCamera != null)
                {
                    legacyCanvas.CreditsCamera.gameObject.SetActive(false);
                }

                legacyCanvas.gameObject.SetActive(false);
            }

            EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem != null)
            {
                eventSystem.gameObject.SetActive(false);
            }

            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvas == null)
                {
                    continue;
                }

                canvas.enabled = false;
                canvas.gameObject.SetActive(false);
            }

            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
            {
                if (listener == null || listener.gameObject.name == "OnlineLocalCamera")
                {
                    continue;
                }

                listener.enabled = false;
            }

            foreach (CinemachineBrain brain in Object.FindObjectsByType<CinemachineBrain>(FindObjectsInactive.Include))
            {
                if (brain == null)
                {
                    continue;
                }

                brain.enabled = false;
            }

            foreach (CinemachineVirtualCamera virtualCamera in Object.FindObjectsByType<CinemachineVirtualCamera>(FindObjectsInactive.Include))
            {
                if (virtualCamera == null)
                {
                    continue;
                }

                virtualCamera.enabled = false;
                virtualCamera.gameObject.SetActive(false);
            }

            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (camera == null || camera.gameObject.name == "OnlineLocalCamera")
                {
                    continue;
                }

                camera.enabled = false;
                camera.gameObject.SetActive(false);
            }

            foreach (string cameraName in MenuCameraNames)
            {
                GameObject cameraObject = GameObject.Find(cameraName);
                if (cameraObject != null)
                {
                    cameraObject.SetActive(false);
                }
            }
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
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light != null && light.type == LightType.Directional && light.gameObject.activeInHierarchy)
                {
                    light.enabled = true;
                    RenderSettings.sun = light;
                    return;
                }
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

        private static void RefreshSceneLightingEnvironment()
        {
            Light activeDirectionalLight = null;
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light != null && light.type == LightType.Directional && light.enabled && light.gameObject.activeInHierarchy)
                {
                    activeDirectionalLight = light;
                    break;
                }
            }

            if (activeDirectionalLight != null)
            {
                RenderSettings.sun = activeDirectionalLight;
            }

            DynamicGI.UpdateEnvironment();
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

        private static GameObject FindDirectionalLightObject(Scene scene)
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
                    if (namedLight != null && namedLight.type == LightType.Directional)
                    {
                        return rootObject;
                    }
                }

                foreach (Light light in rootObject.GetComponentsInChildren<Light>(true))
                {
                    if (light != null && light.type == LightType.Directional)
                    {
                        return light.gameObject;
                    }
                }
            }

            return null;
        }

        private static void DestroyOnlineDirectionalLight()
        {
            if (onlineDirectionalLightInstance == null)
            {
                return;
            }

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
