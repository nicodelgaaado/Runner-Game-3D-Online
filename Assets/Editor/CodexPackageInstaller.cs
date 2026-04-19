using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

[InitializeOnLoad]
public static class CodexPackageInstaller
{
    private const string TriggerArgument = "-codexInstallPackages";
    private const string LogFileName = "unity-package-install.log";
    private static readonly string[] PackageNames =
    {
        "com.unity.inputsystem"
    };

    static CodexPackageInstaller()
    {
        if (!Environment.GetCommandLineArgs().Contains(TriggerArgument))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            try
            {
                Log("Codex package installation trigger detected.");
                InstallRequiredPackages();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Log($"Codex package installation failed: {exception}");
                EditorApplication.Exit(1);
            }
        };
    }

    public static void InstallRequiredPackages()
    {
        ListRequest listRequest = Client.List(true, false);
        WaitForRequest(listRequest);

        HashSet<string> installed = new HashSet<string>(
            listRequest.Result.Select(package => package.name),
            StringComparer.OrdinalIgnoreCase);

        foreach (string packageName in PackageNames)
        {
            if (installed.Contains(packageName))
            {
                Log($"Package already installed: {packageName}");
                continue;
            }

            Log($"Installing package: {packageName}");
            AddRequest addRequest = Client.Add(packageName);
            WaitForRequest(addRequest);
            Log($"Installed package: {packageName}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("Required editor packages are installed.");
    }

    private static void WaitForRequest(Request request)
    {
        while (!request.IsCompleted)
        {
            System.Threading.Thread.Sleep(100);
        }

        if (request.Status == StatusCode.Failure)
        {
            throw new Exception(request.Error?.message ?? "Unknown Unity package manager error.");
        }
    }

    private static void Log(string message)
    {
        Debug.Log(message);
        string path = Path.Combine(Directory.GetCurrentDirectory(), LogFileName);
        File.AppendAllText(path, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
    }
}
