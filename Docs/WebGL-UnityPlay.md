# WebGL Unity Play Shipping Notes

This project ships to WebGL through Unity Play with two tracked build profiles:

- `Assets/Settings/BuildProfiles/Web-Test.asset`
- `Assets/Settings/BuildProfiles/Web-Release.asset`

## Requirements

- The Unity project must be linked to a valid Unity Cloud project.
- Multiplayer still depends on Unity Authentication, Sessions, and Relay.
- Web builds must be launched from HTTPS through Unity Play, not from `file://`.
- Unity batch compile and Unity WebGL builds are the source of truth for this project. External `dotnet build` runs against generated Unity `.csproj` files are not a release gate here.

## Canonical clean check

Run a headless Unity compile check before publishing:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "C:\VSCode\Runner-Game-3D-Online" `
  -logFile "C:\VSCode\Runner-Game-3D-Online\unity-clean-check.log"
```

Expected result:

- Unity exits with code `0`
- the log does not contain script compilation errors

## Profile workflow

Use these menu actions before publishing:

1. `Tools > Codex > WebGL > Activate Web-Test Profile`
2. `Tools > Codex > WebGL > Activate Web-Release Profile`
3. `Tools > Codex > WebGL > Build Web-Test`
4. `Tools > Codex > WebGL > Build Web-Release`

`Web-Test` enables a Development Build style profile and applies `WebGLCompressionFormat.Disabled` for easier browser iteration.

`Web-Release` keeps the release-style profile active and applies `WebGLCompressionFormat.Brotli` for Unity Play publishing.

Both profiles keep the scene list pinned to:

- `Assets/Scenes/Bootstrap.unity`
- `Assets/Scenes/Joc.unity`

## Unity Play publish flow

1. Activate the desired WebGL profile from the `Tools > Codex > WebGL` menu.
2. Build it with `Tools > Codex > WebGL > Build Web-Test` or `Build Web-Release`.
3. Upload the generated local WebGL folder to Unity Play.
4. Launch the uploaded build from the Unity Play HTTPS URL.

## Headless WebGL builds

For CI or terminal-driven validation, use the batch entrypoints with an explicit output path:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "C:\VSCode\Runner-Game-3D-Online" `
  -executeMethod RunnerGame.Editor.WebGLBuildProfileTools.BuildWebTestBatchMode `
  -buildOutputPath "C:\VSCode\Runner-Game-3D-Online\Builds\Web-Test-Batch" `
  -logFile "C:\VSCode\Runner-Game-3D-Online\unity-web-test-build.log"
```

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "C:\VSCode\Runner-Game-3D-Online" `
  -executeMethod RunnerGame.Editor.WebGLBuildProfileTools.BuildWebReleaseBatchMode `
  -buildOutputPath "C:\VSCode\Runner-Game-3D-Online\Builds\Web-Release-Batch" `
  -logFile "C:\VSCode\Runner-Game-3D-Online\unity-web-release-build.log"
```

Notes:

- `-buildOutputPath` is required in batch mode.
- As a fallback, the same path can be provided through `CODEX_BUILD_OUTPUT_PATH`.
- Both batch methods reuse the tracked WebGL build profiles and pinned scene list.

## Browser smoke test

1. Open the Unity Play build in one browser window and host a private match.
2. Open the same Unity Play build in a second window or browser profile and join by code.
3. Verify both peers transition from `Bootstrap` into `Joc`.
4. Verify no Relay transport mismatch warnings appear and the browser build does not expose the legacy `Exit` action.
5. Repeat with `Web-Release` once `Web-Test` passes.
