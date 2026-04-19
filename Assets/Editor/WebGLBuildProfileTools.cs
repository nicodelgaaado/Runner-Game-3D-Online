using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.WebGL;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunnerGame.Editor
{
    internal static class WebGLBuildSceneValidator
    {
        internal const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        internal const string GameplayScenePath = "Assets/Scenes/Joc.unity";
        private const string EditorBuildSettingsAssetPath = "ProjectSettings/EditorBuildSettings.asset";

        private static readonly string[] RequiredScenePaths =
        {
            BootstrapScenePath,
            GameplayScenePath
        };

        private static BuildProfile explicitValidationBuildProfile;

        internal readonly struct SceneResolutionResult
        {
            public SceneResolutionResult(EditorBuildSettingsScene[] scenes, string sceneSourceLabel, bool sceneSourceIsProfile)
            {
                Scenes = scenes ?? Array.Empty<EditorBuildSettingsScene>();
                SceneSourceLabel = string.IsNullOrWhiteSpace(sceneSourceLabel) ? "none" : sceneSourceLabel;
                SceneSourceIsProfile = sceneSourceIsProfile;
            }

            public EditorBuildSettingsScene[] Scenes { get; }
            public string SceneSourceLabel { get; }
            public bool SceneSourceIsProfile { get; }
        }

        internal static void SetExplicitValidationBuildProfile(BuildProfile buildProfile)
        {
            explicitValidationBuildProfile = buildProfile;
        }

        internal static void ClearExplicitValidationBuildProfile(BuildProfile buildProfile = null)
        {
            if (buildProfile == null || explicitValidationBuildProfile == buildProfile)
            {
                explicitValidationBuildProfile = null;
            }
        }

        internal static BuildProfile GetResolvedBuildProfile(BuildProfile buildProfile = null)
        {
            return buildProfile ?? explicitValidationBuildProfile ?? BuildProfile.GetActiveBuildProfile();
        }

        internal static string DescribeResolvedBuildProfileName(BuildProfile buildProfile = null)
        {
            BuildProfile resolvedBuildProfile = GetResolvedBuildProfile(buildProfile);
            return resolvedBuildProfile != null ? resolvedBuildProfile.name : "Platform Defaults";
        }

        internal static SceneResolutionResult ResolveSceneList(BuildProfile buildProfile = null)
        {
            BuildProfile resolvedBuildProfile = GetResolvedBuildProfile(buildProfile);
            if (resolvedBuildProfile != null)
            {
                SceneResolutionResult buildProfileResult = ResolveSceneListFromBuildProfile(resolvedBuildProfile);
                if (HasUsableSceneEntries(buildProfileResult.Scenes))
                {
                    return buildProfileResult;
                }
            }

            if (HasUsableSceneEntries(EditorBuildSettings.scenes))
            {
                return new SceneResolutionResult(
                    CloneSceneList(EditorBuildSettings.scenes),
                    "Platform Defaults (EditorBuildSettings.scenes)",
                    false);
            }

            EditorBuildSettingsScene[] projectSettingsScenes = ReadSceneListFromSerializedAsset(EditorBuildSettingsAssetPath);
            if (HasUsableSceneEntries(projectSettingsScenes))
            {
                return new SceneResolutionResult(
                    projectSettingsScenes,
                    "Platform Defaults (ProjectSettings/EditorBuildSettings.asset)",
                    false);
            }

            return new SceneResolutionResult(Array.Empty<EditorBuildSettingsScene>(), "none", false);
        }

        internal static EditorBuildSettingsScene[] GetValidatedRequiredSceneEntries(BuildProfile buildProfile, out string sceneSourceLabel)
        {
            SceneResolutionResult resolution = ResolveSceneList(buildProfile);
            sceneSourceLabel = resolution.SceneSourceLabel;
            ValidateRequiredSceneContract(resolution, DescribeResolvedBuildProfileName(buildProfile));
            return BuildRequiredSceneEntries();
        }

        internal static EditorBuildSettingsScene[] GetValidatedRequiredSceneEntries(out string sceneSourceLabel)
        {
            return GetValidatedRequiredSceneEntries(null, out sceneSourceLabel);
        }

        internal static string[] GetValidatedRequiredScenePaths(BuildProfile buildProfile, out string sceneSourceLabel)
        {
            EditorBuildSettingsScene[] scenes = GetValidatedRequiredSceneEntries(buildProfile, out sceneSourceLabel);
            return scenes.Select(scene => scene.path).ToArray();
        }

        internal static string[] GetValidatedRequiredScenePaths(out string sceneSourceLabel)
        {
            return GetValidatedRequiredScenePaths(null, out sceneSourceLabel);
        }

        internal static void ValidateRequiredScenes(BuildProfile buildProfile = null)
        {
            GetValidatedRequiredSceneEntries(buildProfile, out _);
        }

        internal static string DescribeSceneList(IEnumerable<EditorBuildSettingsScene> scenes)
        {
            if (scenes == null)
            {
                return "[]";
            }

            return "[" + string.Join(", ", scenes.Select(scene =>
            {
                string path = scene?.path ?? "<null>";
                string enabledSuffix = scene != null && scene.enabled ? string.Empty : " (disabled)";
                return path + enabledSuffix;
            })) + "]";
        }

        internal static string DescribeScenePathList(IEnumerable<string> scenePaths)
        {
            if (scenePaths == null)
            {
                return "[]";
            }

            return "[" + string.Join(", ", scenePaths) + "]";
        }

        internal static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim();
        }

        private static SceneResolutionResult ResolveSceneListFromBuildProfile(BuildProfile buildProfile)
        {
            if (buildProfile == null)
            {
                return new SceneResolutionResult(Array.Empty<EditorBuildSettingsScene>(), "none", false);
            }

            string buildProfileName = buildProfile.name;
            EditorBuildSettingsScene[] scenesFromBuildProfile = GetScenesForBuild(buildProfile);
            if (HasUsableSceneEntries(scenesFromBuildProfile))
            {
                return new SceneResolutionResult(
                    scenesFromBuildProfile,
                    $"{buildProfileName} (GetScenesForBuild)",
                    true);
            }

            EditorBuildSettingsScene[] serializedScenes = GetSerializedScenes(buildProfile);
            if (HasUsableSceneEntries(serializedScenes))
            {
                return new SceneResolutionResult(
                    serializedScenes,
                    $"{buildProfileName} (buildProfile.scenes)",
                    true);
            }

            string buildProfileAssetPath = AssetDatabase.GetAssetPath(buildProfile);
            if (!string.IsNullOrWhiteSpace(buildProfileAssetPath))
            {
                EditorBuildSettingsScene[] yamlScenes = ReadSceneListFromSerializedAsset(buildProfileAssetPath);
                if (HasUsableSceneEntries(yamlScenes))
                {
                    return new SceneResolutionResult(
                        yamlScenes,
                        $"{buildProfileName} (profile asset YAML)",
                        true);
                }
            }

            return new SceneResolutionResult(Array.Empty<EditorBuildSettingsScene>(), $"{buildProfileName} (empty)", true);
        }

        private static EditorBuildSettingsScene[] GetScenesForBuild(BuildProfile buildProfile)
        {
            try
            {
                return FilterUsableScenes(buildProfile.GetScenesForBuild());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[WebGLBuildProfileTools] Failed to read GetScenesForBuild() from build profile '{buildProfile.name}': {exception.Message}");
                return Array.Empty<EditorBuildSettingsScene>();
            }
        }

        private static EditorBuildSettingsScene[] GetSerializedScenes(BuildProfile buildProfile)
        {
            try
            {
                return FilterUsableScenes(buildProfile.scenes);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[WebGLBuildProfileTools] Failed to read serialized scenes from build profile '{buildProfile.name}': {exception.Message}");
                return Array.Empty<EditorBuildSettingsScene>();
            }
        }

        private static EditorBuildSettingsScene[] ReadSceneListFromSerializedAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return Array.Empty<EditorBuildSettingsScene>();
            }

            string fullPath = Path.IsPathRooted(assetPath)
                ? assetPath
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));

            if (!File.Exists(fullPath))
            {
                return Array.Empty<EditorBuildSettingsScene>();
            }

            string[] lines = File.ReadAllLines(fullPath);
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
            bool insideSceneList = false;
            int sceneListIndent = -1;
            bool hasCurrentEntry = false;
            bool currentEnabled = false;
            string currentPath = null;

            void CommitCurrentEntry()
            {
                if (!hasCurrentEntry)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    scenes.Add(new EditorBuildSettingsScene(NormalizePath(currentPath), currentEnabled));
                }

                hasCurrentEntry = false;
                currentEnabled = false;
                currentPath = null;
            }

            foreach (string rawLine in lines)
            {
                string line = rawLine ?? string.Empty;
                string trimmed = line.Trim();

                if (!insideSceneList)
                {
                    if (!trimmed.StartsWith("m_Scenes:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (trimmed.Equals("m_Scenes: []", StringComparison.Ordinal))
                    {
                        return Array.Empty<EditorBuildSettingsScene>();
                    }

                    insideSceneList = true;
                    sceneListIndent = CountLeadingWhitespace(line);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                int lineIndent = CountLeadingWhitespace(line);
                if (lineIndent <= sceneListIndent && !trimmed.StartsWith("-", StringComparison.Ordinal))
                {
                    break;
                }

                if (trimmed.StartsWith("- enabled:", StringComparison.Ordinal))
                {
                    CommitCurrentEntry();
                    hasCurrentEntry = true;
                    currentEnabled = ParseEnabledValue(trimmed.Substring("- enabled:".Length));
                    continue;
                }

                if (trimmed.StartsWith("enabled:", StringComparison.Ordinal))
                {
                    hasCurrentEntry = true;
                    currentEnabled = ParseEnabledValue(trimmed.Substring("enabled:".Length));
                    continue;
                }

                if (trimmed.StartsWith("path:", StringComparison.Ordinal))
                {
                    hasCurrentEntry = true;
                    currentPath = trimmed.Substring("path:".Length).Trim();
                }
            }

            CommitCurrentEntry();
            return FilterUsableScenes(scenes);
        }

        private static bool ParseEnabledValue(string rawValue)
        {
            string normalized = rawValue?.Trim();
            return string.Equals(normalized, "1", StringComparison.Ordinal) ||
                   string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountLeadingWhitespace(string value)
        {
            int count = 0;
            while (count < value.Length && char.IsWhiteSpace(value[count]))
            {
                count++;
            }

            return count;
        }

        private static EditorBuildSettingsScene[] FilterUsableScenes(IEnumerable<EditorBuildSettingsScene> scenes)
        {
            if (scenes == null)
            {
                return Array.Empty<EditorBuildSettingsScene>();
            }

            return scenes
                .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => new EditorBuildSettingsScene(NormalizePath(scene.path), true))
                .ToArray();
        }

        private static bool HasUsableSceneEntries(IEnumerable<EditorBuildSettingsScene> scenes)
        {
            return scenes != null && scenes.Any(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path));
        }

        private static void ValidateRequiredSceneContract(SceneResolutionResult resolution, string buildProfileName)
        {
            EditorBuildSettingsScene[] scenes = resolution.Scenes ?? Array.Empty<EditorBuildSettingsScene>();
            string[] normalizedPaths = scenes.Select(scene => NormalizePath(scene.path)).ToArray();

            if (normalizedPaths.Length != RequiredScenePaths.Length)
            {
                throw BuildRequiredSceneFailure(
                    buildProfileName,
                    resolution.SceneSourceLabel,
                    normalizedPaths,
                    $"Expected exactly {RequiredScenePaths.Length} enabled scenes in the WebGL scene contract.");
            }

            for (int index = 0; index < RequiredScenePaths.Length; index++)
            {
                if (string.Equals(normalizedPaths[index], RequiredScenePaths[index], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                throw BuildRequiredSceneFailure(
                    buildProfileName,
                    resolution.SceneSourceLabel,
                    normalizedPaths,
                    $"Scene at index {index} must be '{RequiredScenePaths[index]}'.");
            }
        }

        private static BuildFailedException BuildRequiredSceneFailure(
            string buildProfileName,
            string sceneSourceLabel,
            IEnumerable<string> resolvedScenePaths,
            string reason)
        {
            string resolvedDescription = DescribeScenePathList(resolvedScenePaths);
            return new BuildFailedException(
                $"Web build must package exactly these scenes in order: {RequiredScenePaths[0]}, {RequiredScenePaths[1]}. " +
                $"Reason: {reason} Active profile: {buildProfileName}. Scene source: {sceneSourceLabel}. Effective scenes: {resolvedDescription}");
        }

        private static EditorBuildSettingsScene[] BuildRequiredSceneEntries()
        {
            return RequiredScenePaths
                .Select(path => new EditorBuildSettingsScene(path, true))
                .ToArray();
        }

        private static EditorBuildSettingsScene[] CloneSceneList(IEnumerable<EditorBuildSettingsScene> scenes)
        {
            if (scenes == null)
            {
                return Array.Empty<EditorBuildSettingsScene>();
            }

            return scenes
                .Where(scene => scene != null)
                .Select(scene => new EditorBuildSettingsScene(scene.path, scene.enabled))
                .ToArray();
        }
    }

    internal static class WebGLBuildDiagnostics
    {
        internal static string DescribeOpenScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            return string.IsNullOrWhiteSpace(activeScene.path)
                ? $"<unsaved:{activeScene.name}>"
                : WebGLBuildSceneValidator.NormalizePath(activeScene.path);
        }
    }

    internal static class WebGLBuildSceneSync
    {
        private static EditorBuildSettingsScene[] previousScenes;
        private static EditorBuildSettingsScene[] previousGlobalScenes;
        private static bool isMirroringSceneList;

        internal static void PrepareForBuild(BuildProfile buildProfile = null)
        {
            EditorBuildSettingsScene[] resolvedScenes = WebGLBuildSceneValidator.GetValidatedRequiredSceneEntries(buildProfile, out string sceneSourceLabel);
            EditorBuildSettingsScene[] normalizedScenes = CloneSceneList(resolvedScenes);

            if (SceneListsMatch(EditorBuildSettings.scenes, normalizedScenes) &&
                SceneListsMatch(EditorBuildSettings.globalScenes, normalizedScenes))
            {
                return;
            }

            if (!isMirroringSceneList)
            {
                previousScenes = CloneSceneList(EditorBuildSettings.scenes);
                previousGlobalScenes = CloneSceneList(EditorBuildSettings.globalScenes);
            }

            EditorBuildSettings.scenes = CloneSceneList(normalizedScenes);
            EditorBuildSettings.globalScenes = CloneSceneList(normalizedScenes);
            isMirroringSceneList = true;

            Debug.Log(
                $"[WebGLBuildProfileTools] Mirrored Web build scenes from {sceneSourceLabel} into EditorBuildSettings/globalScenes. " +
                $"Scenes={WebGLBuildSceneValidator.DescribeSceneList(normalizedScenes)}");
        }

        internal static void RestoreAfterBuild()
        {
            if (!isMirroringSceneList)
            {
                return;
            }

            EditorBuildSettings.scenes = CloneSceneList(previousScenes);
            EditorBuildSettings.globalScenes = CloneSceneList(previousGlobalScenes);
            previousScenes = null;
            previousGlobalScenes = null;
            isMirroringSceneList = false;
            Debug.Log("[WebGLBuildProfileTools] Restored EditorBuildSettings scene lists after WebGL build.");
        }

        private static EditorBuildSettingsScene[] CloneSceneList(IEnumerable<EditorBuildSettingsScene> scenes)
        {
            if (scenes == null)
            {
                return Array.Empty<EditorBuildSettingsScene>();
            }

            return scenes
                .Where(scene => scene != null)
                .Select(scene => new EditorBuildSettingsScene(scene.path, scene.enabled))
                .ToArray();
        }

        private static bool SceneListsMatch(IReadOnlyList<EditorBuildSettingsScene> left, IReadOnlyList<EditorBuildSettingsScene> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                EditorBuildSettingsScene leftScene = left[index];
                EditorBuildSettingsScene rightScene = right[index];
                if (leftScene == null || rightScene == null)
                {
                    return leftScene == rightScene;
                }

                if (leftScene.enabled != rightScene.enabled)
                {
                    return false;
                }

                if (!string.Equals(
                        WebGLBuildSceneValidator.NormalizePath(leftScene.path),
                        WebGLBuildSceneValidator.NormalizePath(rightScene.path),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }

    [InitializeOnLoad]
    internal static class WebGLBuildPlayerWindowHooks
    {
        static WebGLBuildPlayerWindowHooks()
        {
            BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(PrepareBuildPlayerOptions);
            BuildPlayerWindow.RegisterBuildPlayerHandler(HandleBuildPlayer);
        }

        private static BuildPlayerOptions PrepareBuildPlayerOptions(BuildPlayerOptions options)
        {
            if (options.target != BuildTarget.WebGL)
            {
                return options;
            }

            string activeProfileName = WebGLBuildSceneValidator.DescribeResolvedBuildProfileName();
            string openScene = WebGLBuildDiagnostics.DescribeOpenScene();
            string[] explicitScenes = WebGLBuildSceneValidator.GetValidatedRequiredScenePaths(out string sceneSourceLabel);
            options.scenes = explicitScenes;

            Debug.Log(
                $"[WebGLBuildProfileTools] Prepared WebGL build options. " +
                $"ActiveProfile={activeProfileName} SceneSource={sceneSourceLabel} OpenScene={openScene} " +
                $"ExplicitScenes={WebGLBuildSceneValidator.DescribeScenePathList(explicitScenes)} " +
                $"Location='{options.locationPathName}' Target={options.target} Subtarget={options.subtarget} Options={options.options}");

            return options;
        }

        private static void HandleBuildPlayer(BuildPlayerOptions options)
        {
            if (options.target != BuildTarget.WebGL)
            {
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
                return;
            }

            BuildPlayerOptions resolvedOptions = PrepareBuildPlayerOptions(options);
            WebGLBuildSceneSync.PrepareForBuild();
            try
            {
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(resolvedOptions);
            }
            finally
            {
                WebGLBuildSceneSync.RestoreAfterBuild();
            }
        }
    }

    internal sealed class WebGLBuildSceneProcessor : BuildPlayerProcessor
    {
        public override int callbackOrder => 0;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (buildPlayerContext == null || buildPlayerContext.BuildPlayerOptions.target != BuildTarget.WebGL)
            {
                return;
            }

            WebGLBuildSceneSync.PrepareForBuild();
        }
    }

    internal sealed class WebGLBuildScenePreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.WebGL)
            {
                return;
            }

            try
            {
                WebGLBuildSceneSync.PrepareForBuild();
                WebGLBuildSceneValidator.ValidateRequiredScenes();
            }
            catch
            {
                WebGLBuildSceneSync.RestoreAfterBuild();
                throw;
            }
        }
    }

    internal sealed class WebGLBuildScenePostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.WebGL)
            {
                return;
            }

            WebGLBuildSceneSync.RestoreAfterBuild();
        }
    }

    public static class WebGLBuildProfileTools
    {
        private const string WebTestProfilePath = "Assets/Settings/BuildProfiles/Web-Test.asset";
        private const string WebReleaseProfilePath = "Assets/Settings/BuildProfiles/Web-Release.asset";
        private const string BuildOutputPathArgument = "-buildOutputPath";
        private const string BuildOutputPathEnvironmentVariable = "CODEX_BUILD_OUTPUT_PATH";

        [MenuItem("Tools/Codex/WebGL/Activate Web-Test Profile")]
        public static void ActivateWebTestProfile()
        {
            ActivateProfile(WebTestProfilePath, WebGLCompressionFormat.Disabled, "Web-Test", WasmCodeOptimization.BuildTimes);
        }

        [MenuItem("Tools/Codex/WebGL/Activate Web-Release Profile")]
        public static void ActivateWebReleaseProfile()
        {
            ActivateProfile(WebReleaseProfilePath, WebGLCompressionFormat.Brotli, "Web-Release", WasmCodeOptimization.RuntimeSpeed);
        }

        [MenuItem("Tools/Codex/WebGL/Build Web-Test")]
        public static void BuildWebTest()
        {
            BuildWebPlayerInteractive(WebTestProfilePath, WebGLCompressionFormat.Disabled, "Web-Test", WasmCodeOptimization.BuildTimes, BuildOptions.Development);
        }

        [MenuItem("Tools/Codex/WebGL/Build Web-Release")]
        public static void BuildWebRelease()
        {
            BuildWebPlayerInteractive(WebReleaseProfilePath, WebGLCompressionFormat.Brotli, "Web-Release", WasmCodeOptimization.RuntimeSpeed, BuildOptions.None);
        }

        public static void ActivateWebTestProfileBatchMode()
        {
            ActivateWebTestProfile();
        }

        public static void ActivateWebReleaseProfileBatchMode()
        {
            ActivateWebReleaseProfile();
        }

        public static void BuildWebTestBatchMode()
        {
            BuildWebPlayerBatchMode(WebTestProfilePath, WebGLCompressionFormat.Disabled, "Web-Test", WasmCodeOptimization.BuildTimes, BuildOptions.Development);
        }

        public static void BuildWebReleaseBatchMode()
        {
            BuildWebPlayerBatchMode(WebReleaseProfilePath, WebGLCompressionFormat.Brotli, "Web-Release", WasmCodeOptimization.RuntimeSpeed, BuildOptions.None);
        }

        private static void ActivateProfile(string profilePath, WebGLCompressionFormat compressionFormat, string profileLabel, WasmCodeOptimization codeOptimization)
        {
            BuildProfile profile = LoadBuildProfile(profilePath);

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                throw new InvalidOperationException("Failed to switch the active build target to WebGL.");
            }

            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.compressionFormat = compressionFormat;
            UserBuildSettings.codeOptimization = codeOptimization;
            BuildProfile.SetActiveBuildProfile(profile);
            AssetDatabase.SaveAssets();

            Debug.Log($"Activated {profileLabel} build profile and applied WebGL compression '{compressionFormat}'.");
        }

        private static void BuildWebPlayerInteractive(string profilePath, WebGLCompressionFormat compressionFormat, string profileLabel, WasmCodeOptimization codeOptimization, BuildOptions buildOptions)
        {
            string defaultFolder = Path.Combine("Builds", profileLabel);
            string locationPath = EditorUtility.SaveFolderPanel($"Build {profileLabel}", defaultFolder, string.Empty);
            if (string.IsNullOrWhiteSpace(locationPath))
            {
                return;
            }

            BuildWebPlayer(profilePath, compressionFormat, profileLabel, codeOptimization, buildOptions, locationPath);
        }

        private static void BuildWebPlayerBatchMode(string profilePath, WebGLCompressionFormat compressionFormat, string profileLabel, WasmCodeOptimization codeOptimization, BuildOptions buildOptions)
        {
            string locationPath = ResolveBatchBuildOutputPath();
            BuildWebPlayer(profilePath, compressionFormat, profileLabel, codeOptimization, buildOptions, locationPath);
        }

        private static void BuildWebPlayer(string profilePath, WebGLCompressionFormat compressionFormat, string profileLabel, WasmCodeOptimization codeOptimization, BuildOptions buildOptions, string locationPath)
        {
            if (string.IsNullOrWhiteSpace(locationPath))
            {
                throw new InvalidOperationException($"Missing build output path for {profileLabel}.");
            }

            BuildProfile profile = LoadBuildProfile(profilePath);
            ActivateProfile(profilePath, compressionFormat, profileLabel, codeOptimization);

            string[] explicitScenes = WebGLBuildSceneValidator.GetValidatedRequiredScenePaths(profile, out string sceneSourceLabel);
            string resolvedLocationPath = Path.GetFullPath(locationPath);
            Directory.CreateDirectory(resolvedLocationPath);

            Debug.Log(
                $"[WebGLBuildProfileTools] Building {profileLabel}. " +
                $"ActiveProfile={profile.name} SceneSource={sceneSourceLabel} OpenScene={WebGLBuildDiagnostics.DescribeOpenScene()} " +
                $"ExplicitScenes={WebGLBuildSceneValidator.DescribeScenePathList(explicitScenes)} Location='{resolvedLocationPath}' Options={buildOptions}");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = explicitScenes,
                target = BuildTarget.WebGL,
                locationPathName = resolvedLocationPath,
                options = buildOptions
            };

            WebGLBuildSceneValidator.SetExplicitValidationBuildProfile(profile);
            try
            {
                WebGLBuildSceneSync.PrepareForBuild(profile);
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException($"{profileLabel} build failed with result '{report.summary.result}'.");
                }
            }
            finally
            {
                WebGLBuildSceneValidator.ClearExplicitValidationBuildProfile(profile);
                WebGLBuildSceneSync.RestoreAfterBuild();
            }

            Debug.Log($"{profileLabel} build completed at '{resolvedLocationPath}'.");
        }

        private static BuildProfile LoadBuildProfile(string profilePath)
        {
            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
            if (profile == null)
            {
                throw new InvalidOperationException($"Missing build profile asset at '{profilePath}'.");
            }

            return profile;
        }

        private static string ResolveBatchBuildOutputPath()
        {
            string[] commandLineArgs = Environment.GetCommandLineArgs();
            for (int index = 0; index < commandLineArgs.Length - 1; index++)
            {
                if (!string.Equals(commandLineArgs[index], BuildOutputPathArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string candidate = commandLineArgs[index + 1];
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }

            string environmentValue = Environment.GetEnvironmentVariable(BuildOutputPathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue;
            }

            throw new InvalidOperationException(
                $"Missing required build output path. Provide '{BuildOutputPathArgument} <absolute path>' or set '{BuildOutputPathEnvironmentVariable}'.");
        }
    }
}
