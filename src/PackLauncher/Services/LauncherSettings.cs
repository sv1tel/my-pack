using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PackLauncher.Services;

public enum AfterLaunchAction { Minimize, Close, Nothing }

public enum LauncherTheme { System, Light, Dark }

public sealed class LauncherSettings
{
    public int PcRamGb { get; set; }

    public string Nickname { get; set; } = "";

    public string? GameDirectory { get; set; }

    public int WindowWidth { get; set; } = 1280;

    public int WindowHeight { get; set; } = 720;

    public bool FullScreen { get; set; }

    public AfterLaunchAction AfterLaunch { get; set; } = AfterLaunchAction.Minimize;

    public bool AutoJvmFlags { get; set; } = true;

    public string CustomJvmFlags { get; set; } = "";

    public string? Language { get; set; }

    public LauncherTheme Theme { get; set; } = LauncherTheme.System;

    [JsonIgnore]
    public string GameDir => string.IsNullOrWhiteSpace(GameDirectory) ? AppPaths.DefaultGameDir : GameDirectory!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions)
                       ?? new LauncherSettings();
        }
        catch
        {
        }

        var settings = new LauncherSettings();

        try
        {
            if (File.Exists(AppPaths.InstallerDefaultsFile))
            {
                var defaults = JsonSerializer.Deserialize<InstallerDefaults>(
                    File.ReadAllText(AppPaths.InstallerDefaultsFile), JsonOptions);
                if (defaults is { PcRamGb: > 0 })
                    settings.PcRamGb = RamPlanner.NormalizeProfile(defaults.PcRamGb);
                if (!string.IsNullOrWhiteSpace(defaults?.Language))
                    settings.Language = defaults.Language;
            }
        }
        catch
        {
        }

        settings.Save();
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch
        {
        }
    }

    private sealed class InstallerDefaults
    {
        public int PcRamGb { get; set; }

        public string? Language { get; set; }
    }
}
