# AuraWall

Audio-reactive live wallpaper for Windows 10/11 (C# / .NET 8, WPF + SkiaSharp, NAudio WASAPI loopback).

## Build & run
```powershell
winget install Microsoft.DotNet.SDK.8          # once
cd AuraWall
dotnet restore
dotnet run -c Release
# release build:
dotnet publish -c Release -r win-x64 --self-contained false -o publish
# single exe with runtime bundled:
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```
NuGet: `NAudio 2.2.1`, `SkiaSharp.Views.WPF 2.88.8` (restored automatically).

## Controls
- Tray icon: right-click menu (full-screen, preset, sensitivity, clock, monitor, autostart, exit). Double-click = toggle full-screen.
- `Ctrl+Shift+V` toggles Full-Screen / Wallpaper mode.
- In full-screen: `Esc` exit, `1/2/3` or `Tab/←/→` presets, `↑/↓` sensitivity, `H` clock, `M` media.
- Settings persist to `%AppData%\AuraWall\settings.json`.

## Tuning
- Band gains / dB window: `Audio/LoopbackAnalyzer.cs` (`MinDb`, `DbRange`, gains in `Analyze`).
- Attack/release smoothing: `Visualizer.Update` (`Smooth(...)` rates).

## No local .NET? Build on GitHub
Push this folder to a GitHub repo (keep `.github/workflows/build.yml`). Open the Actions tab, run "Build AuraWall", then download the `AuraWall-win-x64` artifact. It contains a self-contained `AuraWall.exe`.
