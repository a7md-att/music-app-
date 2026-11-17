using System;
using System.IO;
using System.Text.Json;

namespace AuraWall;

public enum VisualizerPreset { RadialSpectrum, FluidWaveforms, ParticleField }

public enum ClockPosition
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, Center, MiddleRight,
    BottomLeft, BottomCenter, BottomRight
}

public sealed class AppSettings
{
    public VisualizerPreset Preset { get; set; } = VisualizerPreset.RadialSpectrum;
    public float Sensitivity { get; set; } = 1f;          // 0.1 .. 3.0
    public bool ShowClock { get; set; } = true;
    public bool Use24Hour { get; set; } = true;
    public float ClockOpacity { get; set; } = 0.85f;
    public int ClockWeight { get; set; } = 300;           // 300 light, 400 regular, 700 bold
    public ClockPosition ClockPosition { get; set; } = ClockPosition.Center;
    public bool ShowMedia { get; set; } = true;
    public int MonitorIndex { get; set; } = -2;           // -2 primary, -1 span all, >=0 specific

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuraWall", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
