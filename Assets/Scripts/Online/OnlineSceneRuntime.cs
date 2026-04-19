using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public static class OnlineSceneRuntime
    {
        private const string BootstrapSceneName = "Bootstrap";
        private const string GameplaySceneName = "Joc";

        private static bool redirectingToBootstrap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoadHooks()
        {
            redirectingToBootstrap = false;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            BootstrapOnlineScene(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
        {
            BootstrapOnlineScene(scene);
        }

        private static void BootstrapOnlineScene(Scene scene)
        {
            if (scene.name == BootstrapSceneName)
            {
                redirectingToBootstrap = false;
                LegacySceneAdapter.HandleSceneLoaded(scene);
                return;
            }

            if (scene.name != GameplaySceneName)
            {
                return;
            }

            if (ShouldRedirectStandaloneGameplayScene(scene))
            {
                LegacySceneAdapter.SuppressLegacyLocalRuntime(scene);
                RedirectToBootstrap();
                return;
            }

            LegacySceneAdapter.HandleSceneLoaded(scene);
            EnsureGameplaySceneReady();
        }

        public static bool DisableLegacyRuntimeComponentIfBlocked(UnityEngine.Behaviour behaviour, bool deactivateGameObject = false)
        {
            if (behaviour == null || !ShouldBlockLegacyLocalRuntime(behaviour.gameObject.scene))
            {
                return false;
            }

            behaviour.enabled = false;
            if (deactivateGameObject)
            {
                behaviour.gameObject.SetActive(false);
            }

            return true;
        }

        public static bool ShouldBlockLegacyLocalRuntime(Scene scene)
        {
            return scene.IsValid()
                && scene.name == GameplaySceneName
                && !IsLegacyLocalRuntimeAllowed(scene);
        }

        public static bool ShouldUseLegacyGameplayCameraRig(Scene scene)
        {
            return scene.IsValid()
                && scene.name == GameplaySceneName
                && IsLegacyLocalRuntimeAllowed(scene);
        }

        internal static bool EnsureGameplaySceneReady(NetworkRunner networkRunner = null)
        {
            if (!TryGetGameplayScene(out Scene scene))
            {
                return false;
            }

            if (ShouldRedirectStandaloneGameplayScene(scene, networkRunner))
            {
                LegacySceneAdapter.SuppressLegacyLocalRuntime(scene);
                RedirectToBootstrap();
                return false;
            }

            NetworkRunner activeRunner = networkRunner ?? SessionRuntime.Runner;
            if (activeRunner == null || !activeRunner.IsRunning)
            {
                return false;
            }

            LegacySceneAdapter.HandleSceneLoaded(scene);
            LegacySceneAdapter.InitializeForOnlineScene();
            EnsureSceneObject<LocalMatchHudController>(scene, "LocalMatchHud");
            return true;
        }

        internal static bool IsGameplaySceneInitialized(NetworkRunner networkRunner = null)
        {
            if (!TryGetGameplayScene(out _))
            {
                return false;
            }

            NetworkRunner activeRunner = networkRunner ?? SessionRuntime.Runner;
            if (activeRunner == null || !activeRunner.IsRunning)
            {
                return false;
            }

            return Object.FindAnyObjectByType<LocalMatchHudController>(FindObjectsInactive.Include) != null;
        }

        private static bool TryGetGameplayScene(out Scene scene)
        {
            scene = SceneManager.GetSceneByName(GameplaySceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                return true;
            }

            scene = SceneManager.GetActiveScene();
            return scene.IsValid() && scene.isLoaded && scene.name == GameplaySceneName;
        }

        private static void EnsureSceneObject<T>(Scene scene, string objectName) where T : Component
        {
            if (Object.FindAnyObjectByType<T>(FindObjectsInactive.Include) != null)
            {
                return;
            }

            GameObject sceneObject = new GameObject(objectName);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.MoveGameObjectToScene(sceneObject, scene);
            }
            sceneObject.AddComponent<T>();
        }

        private static bool HasActiveFusionSession(NetworkRunner networkRunner = null)
        {
            NetworkRunner activeRunner = networkRunner ?? SessionRuntime.Runner;
            return activeRunner != null && activeRunner.IsRunning;
        }

        private static bool IsLegacyLocalRuntimeAllowed(Scene _)
        {
            return false;
        }

        private static bool ShouldRedirectStandaloneGameplayScene(Scene scene, NetworkRunner networkRunner = null)
        {
            return scene.IsValid()
                && scene.isLoaded
                && scene.name == GameplaySceneName
                && !HasActiveFusionSession(networkRunner);
        }

        private static void RedirectToBootstrap()
        {
            if (redirectingToBootstrap || SceneManager.GetActiveScene().name == BootstrapSceneName)
            {
                return;
            }

            redirectingToBootstrap = true;
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
        }
    }
}
