using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEditor.WebGL;
using System;
using System.IO;

namespace RunnerGame.Editor
{
    public static class WebGLBuildProfileTools
    {
        private const string WebTestProfilePath = "Assets/Settings/BuildProfiles/Web-Test.asset";
        private const string WebReleaseProfilePath = "Assets/Settings/BuildProfiles/Web-Release.asset";
        private const string BuildOutputPathArgument = "-buildOutputPath";
        private const string BuildOutputPathEnvironmentVariable = "CODEX_BUILD_OUTPUT_PATH";
        private static readonly EditorBuildSettingsScene[] WebScenes =
        {
            new("Assets/Scenes/Bootstrap.unity", true),
            new("Assets/Scenes/Joc.unity", true)
        };

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
            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
            if (profile == null)
            {
                throw new InvalidOperationException($"Missing build profile asset at '{profilePath}'.");
            }

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                throw new InvalidOperationException("Failed to switch the active build target to WebGL.");
            }

            SyncSceneLists();
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.compressionFormat = compressionFormat;
            UserBuildSettings.codeOptimization = codeOptimization;
            BuildProfile.SetActiveBuildProfile(profile);
            AssetDatabase.SaveAssets();

            Debug.Log($"Activated {profileLabel} build profile and applied WebGL compression '{compressionFormat}'.");
        }

        private static void BuildWebPlayerInteractive(string profilePath, WebGLCompressionFormat compressionFormat, string profileLabel, WasmCodeOptimization codeOptimization, BuildOptions buildOptions)
        {
            string defaultFolder = System.IO.Path.Combine("Builds", profileLabel);
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

            ActivateProfile(profilePath, compressionFormat, profileLabel, codeOptimization);

            string resolvedLocationPath = Path.GetFullPath(locationPath);
            Directory.CreateDirectory(resolvedLocationPath);
            Debug.Log($"Building {profileLabel} to '{resolvedLocationPath}' using profile '{profilePath}'.");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenePaths(),
                target = BuildTarget.WebGL,
                locationPathName = resolvedLocationPath,
                options = buildOptions
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"{profileLabel} build failed with result '{report.summary.result}'.");
            }

            Debug.Log($"{profileLabel} build completed at '{resolvedLocationPath}'.");
        }

        private static void SyncSceneLists()
        {
            EditorBuildSettings.scenes = WebScenes;
        }

        private static string[] GetEnabledScenePaths()
        {
            string[] scenePaths = new string[WebScenes.Length];
            for (int index = 0; index < WebScenes.Length; index++)
            {
                scenePaths[index] = WebScenes[index].path;
            }

            return scenePaths;
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
