using System.IO;
using System.Reflection;

namespace PackLauncher.Services;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppConfig.DataFolderName);

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static string DefaultGameDir => Path.Combine(DataDir, "game");

    public static string InstallerDefaultsFile => Path.Combine(AppContext.BaseDirectory, "defaults.json");

    public static Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";
}
