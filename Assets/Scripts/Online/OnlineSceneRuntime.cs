using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunnerGame.Online
{
    public static class OnlineSceneRuntime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoadHooks()
        {
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
            LegacySceneAdapter.HandleSceneLoaded(scene);

            if (scene.name != "Joc")
            {
                return;
            }

            EnsureGameplaySceneReady();
        }

        internal static bool EnsureGameplaySceneReady(NetworkRunner networkRunner = null)
        {
            if (!TryGetGameplayScene(out Scene scene))
            {
                return false;
            }

            LegacySceneAdapter.HandleSceneLoaded(scene);

            NetworkRunner activeRunner = networkRunner ?? SessionRuntime.Runner;
            if (activeRunner == null || !activeRunner.IsRunning)
            {
                return false;
            }

            LegacySceneAdapter.InitializeForOnlineScene();
            EnsureSceneObject<LocalMatchHudController>(scene, "LocalMatchHud");
            return true;
        }

        private static bool TryGetGameplayScene(out Scene scene)
        {
            scene = SceneManager.GetSceneByName("Joc");
            if (scene.IsValid() && scene.isLoaded)
            {
                return true;
            }

            scene = SceneManager.GetActiveScene();
            return scene.IsValid() && scene.isLoaded && scene.name == "Joc";
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
    }
}
