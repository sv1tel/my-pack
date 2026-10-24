using System.Diagnostics;
using System.IO;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;

namespace PackLauncher.Services;

public sealed class GameService
{
    public async Task<Process> PrepareAsync(
        PackManifest manifest,
        MSession session,
        LauncherSettings settings,
        IProgress<(string Text, double Fraction)> progress,
        CancellationToken ct = default)
    {
        var gameDir = settings.GameDir;
        Directory.CreateDirectory(gameDir);

        var launcher = new MinecraftLauncher(new MinecraftPath(gameDir));

        var stage = Loc.T("status_game_files");
        var fileProgress = new Progress<InstallerProgressChangedEventArgs>(e =>
        {
            if (e.TotalTasks > 0)
                progress.Report(($"{stage}: {e.ProgressedTasks}/{e.TotalTasks}", (double)e.ProgressedTasks / e.TotalTasks));
        });
        var byteProgress = new Progress<ByteProgress>(e =>
        {
            if (e.TotalBytes > 0)
                progress.Report(($"{stage}: {e.ProgressedBytes / 1048576} / {e.TotalBytes / 1048576} MB", (double)e.ProgressedBytes / e.TotalBytes));
        });

        stage = Loc.T("status_neoforge");
        progress.Report((stage + "…", 0));
        var neoForge = new NeoForgeInstaller(launcher);
        var installOptions = new ForgeInstallOptions
        {
            FileProgress = fileProgress,
            ByteProgress = byteProgress,
            CancellationToken = ct,
            SkipIfAlreadyInstalled = true
        };

        string versionName;
        var pinned = string.IsNullOrWhiteSpace(manifest.NeoForge) || manifest.NeoForge == "latest"
            ? null
            : $"neoforge-{manifest.NeoForge}";
        var versionsDir = Path.Combine(gameDir, "versions");

        if (pinned is not null && File.Exists(Path.Combine(versionsDir, pinned, pinned + ".json")))
        {
            versionName = pinned;
        }
        else
        {
            try
            {
                versionName = pinned is null
                    ? await neoForge.Install(manifest.Minecraft, installOptions)
                    : await neoForge.Install(manifest.Minecraft, manifest.NeoForge!, installOptions);
            }
            catch when (pinned is null && FindInstalledNeoForge(versionsDir) is not null)
            {
                versionName = FindInstalledNeoForge(versionsDir)!;
            }
        }

        var maxRam = RamPlanner.GetMaxHeapMb(settings);
        var options = new MLaunchOption
        {
            Session = session,
            MaximumRamMb = maxRam,
            MinimumRamMb = RamPlanner.GetMinHeapMb(maxRam),
            ExtraJvmArguments = [new MArgument { Values = RamPlanner.BuildJvmFlags(settings) }],
            FullScreen = settings.FullScreen,
            ScreenWidth = settings.FullScreen ? 0 : settings.WindowWidth,
            ScreenHeight = settings.FullScreen ? 0 : settings.WindowHeight,
            GameLauncherName = AppConfig.LauncherName,
            GameLauncherVersion = AppPaths.CurrentVersionText
        };

        stage = Loc.T("status_game_files");
        progress.Report((stage + "…", 0));
        return await launcher.InstallAndBuildProcessAsync(versionName, options, fileProgress, byteProgress, ct);
    }

    private static string? FindInstalledNeoForge(string versionsDir) =>
        Directory.Exists(versionsDir)
            ? Directory.EnumerateDirectories(versionsDir, "neoforge-*")
                .Select(d => Path.GetFileName(d))
                .Where(n => File.Exists(Path.Combine(versionsDir, n, n + ".json")))
                .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
}
