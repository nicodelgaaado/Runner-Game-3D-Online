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

            if (!SessionRuntime.HasSession || scene.name != "Joc")
            {
                return;
            }

            LegacySceneAdapter.InitializeForOnlineScene();
            EnsureSceneObject<LocalMatchHudController>("LocalMatchHud");
        }

        private static void EnsureSceneObject<T>(string objectName) where T : Component
        {
            if (Object.FindAnyObjectByType<T>(FindObjectsInactive.Include) != null)
            {
                return;
            }

            GameObject sceneObject = new GameObject(objectName);
            sceneObject.AddComponent<T>();
        }
    }
}
