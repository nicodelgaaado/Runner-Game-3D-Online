using System;
using System.IO;
using RunnerGame.Online;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CodexOnlineAssetBuilder
{
    private const string PlayerPrefabPath = "Assets/Resources/RunnerNetworkPlayer.prefab";
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string BuilderLogPath = "codex-online-builder.log";

    [MenuItem("Codex/Build Online Multiplayer Assets")]
    public static void BuildOnlineAssets()
    {
        Log("BuildOnlineAssets started.");
        try
        {
            EnsureFolders();
            BuildPlayerPrefab();
            BuildBootstrapScene();
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
        Log("Ensuring folders.");
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
            Log("Created Assets/Resources.");
        }
    }

    private static void BuildPlayerPrefab()
    {
        Log("Building player prefab.");
        GameObject root = new GameObject("RunnerNetworkPlayer");
        root.AddComponent<Rigidbody>();
        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 2.6f, 0f);
        collider.height = 5f;
        collider.radius = 1.3f;

        AddPackageComponent(root, "Unity.Netcode.NetworkObject, Unity.Netcode.Runtime");
        AddPackageComponent(root, "Unity.Netcode.Components.NetworkTransform, Unity.Netcode.Components");

        root.AddComponent<RunnerMotor>();
        root.AddComponent<RunnerInputAdapter>();
        root.AddComponent<RunnerPresentation>();
        root.AddComponent<LocalPlayerCameraBinder>();
        root.AddComponent<RunnerNetworkPlayer>();

        PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        Log("Saved player prefab.");
    }

    private static void BuildBootstrapScene()
    {
        Log("Building bootstrap scene.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cameraObject = new GameObject("BootstrapCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        cameraObject.AddComponent<AudioListener>();
        cameraObject.transform.position = new Vector3(0f, 8f, -14f);
        cameraObject.transform.rotation = Quaternion.Euler(20f, 0f, 0f);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject bootstrapper = new GameObject("SessionBootstrapper");
        bootstrapper.AddComponent<SessionBootstrapper>();

        EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        Log("Saved bootstrap scene.");
    }

    private static void ConfigureBuildSettings()
    {
        Log("Configuring build settings.");
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(BootstrapScenePath, true),
            new EditorBuildSettingsScene("Assets/Scenes/Joc.unity", true)
        };

        Log("Configured build settings.");
    }

    private static void AddPackageComponent(GameObject target, string assemblyQualifiedTypeName)
    {
        Type type = Type.GetType(assemblyQualifiedTypeName);
        if (type == null)
        {
            throw new InvalidOperationException($"Missing package type: {assemblyQualifiedTypeName}");
        }

        target.AddComponent(type);
    }

    private static void Log(string message)
    {
        File.AppendAllText(Path.Combine(Directory.GetCurrentDirectory(), BuilderLogPath), $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
        Debug.Log(message);
    }
}
