using System;
using System.IO;
using Fusion;
using RunnerGame.Online;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CodexOnlineAssetBuilder
{
    private const string PlayerPrefabPath = "Assets/Resources/RunnerNetworkPlayer.prefab";
    private const string RaceManagerPrefabPath = "Assets/Resources/NetworkRaceManager.prefab";
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string GameplayScenePath = "Assets/Scenes/Joc.unity";
    private const string BuilderLogPath = "codex-online-builder.log";

    [MenuItem("Codex/Build Online Multiplayer Assets")]
    public static void BuildOnlineAssets()
    {
        Log("BuildOnlineAssets started.");
        try
        {
            EnsureFolders();
            BuildPlayerPrefab();
            BuildRaceManagerPrefab();
            EnsureBootstrapScene();
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log("BuildOnlineAssets completed successfully.");
        }
        catch (Exception exception)
        {
            Log($"BuildOnlineAssets failed: {exception}");
            throw;
        }
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
            Log("Created Assets/Resources.");
        }
    }

    private static void BuildPlayerPrefab()
    {
        Log("Building Fusion player prefab.");
        GameObject root = new GameObject("RunnerNetworkPlayer");
        root.AddComponent<NetworkObject>();

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 100f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 2.6f, 0f);
        collider.size = new Vector3(2.6f, 5f, 2.6f);

        root.AddComponent<RunnerMotor>();
        root.AddComponent<RunnerInputAdapter>();
        root.AddComponent<RunnerPresentation>();
        root.AddComponent<LocalPlayerCameraBinder>();
        root.AddComponent<RunnerNetworkPlayer>();

        PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        Log("Saved Fusion player prefab.");
    }

    private static void BuildRaceManagerPrefab()
    {
        Log("Building Fusion race manager prefab.");
        GameObject root = new GameObject("NetworkRaceManager");
        root.AddComponent<NetworkObject>();
        root.AddComponent<NetworkRaceManager>();
        PrefabUtility.SaveAsPrefabAsset(root, RaceManagerPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        Log("Saved Fusion race manager prefab.");
    }

    private static void EnsureBootstrapScene()
    {
        Log("Ensuring bootstrap scene contains SessionBootstrapper.");
        var scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
        SessionBootstrapper bootstrapper = UnityEngine.Object.FindAnyObjectByType<SessionBootstrapper>(FindObjectsInactive.Include);
        if (bootstrapper == null)
        {
            GameObject bootstrapperObject = new GameObject("SessionBootstrapper");
            bootstrapperObject.AddComponent<SessionBootstrapper>();
            Log("Created SessionBootstrapper in Bootstrap scene.");
        }

        EditorSceneManager.SaveScene(scene, BootstrapScenePath);
    }

    private static void ConfigureBuildSettings()
    {
        Log("Configuring build settings.");
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(BootstrapScenePath, true),
            new EditorBuildSettingsScene(GameplayScenePath, true)
        };
    }

    private static void Log(string message)
    {
        File.AppendAllText(Path.Combine(Directory.GetCurrentDirectory(), BuilderLogPath), $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
        Debug.Log(message);
    }
}
