using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using CmlLib.Core.Auth;
using PackLauncher.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PackLauncher;

public partial class MainWindow
{
    private enum ButtonMode { Busy, Play, Update, Running }

    private readonly HttpClient _http;
    private readonly ModpackUpdater _packUpdater;
    private readonly LauncherUpdater _launcherUpdater;
    private readonly GameService _game = new();

    private PackManifest? _manifest;
    private UpdatePlan? _plan;
    private LauncherRelease? _release;
    private bool _online;
    private string? _offlineReason;
    private Process? _gameProcess;
    private bool _killedByUser;
    private bool _busy;
    private ButtonMode _mode = ButtonMode.Busy;
    private SettingsWindow? _settingsWindow;

    public LauncherSettings Settings { get; }

    public MainWindow()
    {
        Settings = LauncherSettings.Load();
        Loc.I.SetLanguage(Settings.Language);

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppConfig.DataFolderName}/{AppPaths.CurrentVersionText}");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        var api = new GitHubApi(_http);
        _packUpdater = new ModpackUpdater(_http, api);
        _launcherUpdater = new LauncherUpdater(_http, api);

        InitializeComponent();

        Title = AppConfig.LauncherName;
        AppTitleBar.Title = AppConfig.LauncherName;

        ApplyTheme(Settings.Theme);
        Loc.I.LanguageChanged += OnLanguageChanged;
        NickBox.Text = Settings.Nickname;
        _busy = true;
        RefreshMainButton();

        Loaded += OnLoaded;
        ContentRendered += (_, _) => LockWindowSize();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetStatus(Loc.T("status_checking"));
        try
        {
            await CheckForUpdatesAsync();
        }
        finally
        {
            SetBusy(false);
            ShowIdleStatus();
        }
    }

    private void LockWindowSize()
    {
        SizeToContent = SizeToContent.Manual;
        MinWidth = MaxWidth = ActualWidth;
        MinHeight = MaxHeight = ActualHeight;
    }

    public bool IsBusy => _busy || _gameProcess is not null;

    private bool PackUpdateAvailable
    {
        get
        {
            if (!_online || _manifest is null)
                return false;
            var installed = ModpackUpdater.GetInstalledVersion(Settings.GameDir);
            return installed is not null && !string.Equals(installed, _manifest.Version, StringComparison.Ordinal);
        }
    }

    private void RefreshMainButton()
    {
        if (_gameProcess is { HasExited: false })
            _mode = ButtonMode.Running;
        else if (_busy)
            _mode = ButtonMode.Busy;
        else if (_release is not null || PackUpdateAvailable)
            _mode = ButtonMode.Update;
        else
            _mode = ButtonMode.Play;

        (string text, SymbolRegular icon, ControlAppearance look) = _mode switch
        {
            ButtonMode.Running => (Loc.T("close"), SymbolRegular.Dismiss24, ControlAppearance.Danger),
            ButtonMode.Busy => (Loc.T("loading"), SymbolRegular.ArrowSync24, ControlAppearance.Primary),
            ButtonMode.Update => (Loc.T("update"), SymbolRegular.ArrowDownload24, ControlAppearance.Primary),
            _ => (Loc.T("play"), SymbolRegular.Play24, ControlAppearance.Primary)
        };

        MainButton.Content = text;
        MainButton.Icon = new SymbolIcon(icon);
        MainButton.Appearance = look;
        MainButton.IsEnabled = _mode != ButtonMode.Busy;
        NickBox.IsEnabled = !_busy && _mode != ButtonMode.Running;

        VersionText.Text = Loc.F("version", AppPaths.CurrentVersionText);
    }

    private void OnLanguageChanged()
    {
        RefreshMainButton();
        if (!_busy)
            ShowIdleStatus();
    }

    private async void MainButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_mode)
        {
            case ButtonMode.Running:
                CloseGame();
                break;
            case ButtonMode.Update:
                await UpdateAsync();
                break;
            case ButtonMode.Play:
                await PlayAsync();
                break;
        }
    }

    private static bool IsRepoConfigured => AppConfig.GitHubOwner != "YOUR_GITHUB_NAME";

    private async Task CheckForUpdatesAsync(bool fullCheck = false)
    {
        if (!IsRepoConfigured)
        {
            _online = false;
            return;
        }

        _offlineReason = null;
        try
        {
            _release = await _launcherUpdater.CheckAsync();
        }
        catch (GitHubRateLimitException ex)
        {
            _offlineReason = ex.Message;
        }
        catch
        {
        }

        try
        {
            _manifest = await _packUpdater.FetchManifestAsync();
            _plan = await _packUpdater.PlanAsync(_manifest, Settings.GameDir, fullCheck);
            _online = true;
        }
        catch (Exception ex)
        {
            if (ex is GitHubRateLimitException)
                _offlineReason = ex.Message;
            else
                LogError("update check", ex);
            _manifest = ModpackUpdater.LoadCachedManifest(Settings.GameDir) ?? _manifest;
            _plan = null;
            _online = false;
        }
    }

    private void ShowIdleStatus()
    {
        if (_gameProcess is not null)
            SetStatus(Loc.T("status_running"));
        else if (!IsRepoConfigured)
            SetStatus(Loc.T("repo_not_configured"));
        else if (!_online)
            SetStatus(_offlineReason ?? Loc.T("status_offline"));
        else if (_release is not null)
            SetStatus(Loc.F("status_launcher_update", _release.Version.ToString(3)));
        else if (PackUpdateAvailable)
            SetStatus(Loc.F("status_pack_update", _manifest!.Version));
        else
            SetStatus(null);
    }

    private async Task ApplyPlanAsync(UpdatePlan plan)
    {
        ShowProgress(true);
        await _packUpdater.ApplyAsync(plan, Settings.GameDir, CreateProgress());
        _plan = null;
    }

    private async Task UpdateAsync()
    {
        if (_release is not null)
        {
            await InstallLauncherUpdateAsync();
            return;
        }

        SetBusy(true);
        try
        {
            await CheckForUpdatesAsync();
            if (_plan is not null && _plan.HasWork)
                await ApplyPlanAsync(_plan);
            ShowProgress(false);
            SetBusy(false);
            SetStatus(_manifest is null ? null : Loc.F("status_pack_updated", _manifest.Version));
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("err_update"), ex);
            ShowProgress(false);
            SetBusy(false);
            ShowIdleStatus();
        }
    }

    private async Task<bool> InstallLauncherUpdateAsync(IProgress<double>? extraProgress = null)
    {
        if (_release is null) return false;
        SetBusy(true);
        ShowProgress(true);
        try
        {
            var progress = new Progress<double>(p =>
            {
                SetStatus(Loc.F("status_launcher_download", p));
                ProgressBar.Value = p;
                extraProgress?.Report(p);
            });
            await _launcherUpdater.DownloadAndInstallAsync(_release, progress);
            return true;
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("err_update"), ex);
            ShowProgress(false);
            SetBusy(false);
            ShowIdleStatus();
            return false;
        }
    }

    private async Task PlayAsync()
    {
        var nickname = NickBox.Text.Trim();
        if (!IsValidNickname(nickname))
        {
            SetStatus(Loc.T("nick_invalid"));
            NickBox.Focus();
            return;
        }

        SetBusy(true);
        ShowProgress(true);
        try
        {
            var session = MSession.CreateOfflineSession(nickname);

            SetStatus(Loc.T("status_checking"));
            await CheckForUpdatesAsync();
            if (_plan is not null && _plan.HasWork)
                await ApplyPlanAsync(_plan);

            var manifest = _manifest ?? ModpackUpdater.LoadCachedManifest(Settings.GameDir)
                ?? throw new InvalidOperationException(Loc.T("no_manifest"));

            try
            {
                ModpackUpdater.ApplyGameDefaults(manifest, Settings.GameDir);
            }
            catch (Exception ex)
            {
                LogError("resource packs", ex);
            }

            var process = await _game.PrepareAsync(manifest, session, Settings, CreateProgress());

            SetStatus(Loc.T("status_starting"));
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.Invoke(() => OnGameExited(process));
            process.Start();

            _gameProcess = process;
            _killedByUser = false;
            ShowProgress(false);
            SetStatus(Loc.T("status_running"));
            _busy = false;
            RefreshMainButton();

            switch (Settings.AfterLaunch)
            {
                case AfterLaunchAction.Minimize:
                    WindowState = WindowState.Minimized;
                    break;
                case AfterLaunchAction.Close:
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("err_launch"), ex);
            ShowProgress(false);
            SetBusy(false);
            ShowIdleStatus();
        }
    }

    private void CloseGame()
    {
        try
        {
            _killedByUser = true;
            _gameProcess?.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private void OnGameExited(Process process)
    {
        if (process != _gameProcess)
            return;

        _gameProcess = null;
        RefreshMainButton();
        ShowIdleStatus();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();

        int code;
        try { code = process.ExitCode; } catch { code = 0; }

        if (code != 0 && !_killedByUser)
        {
            var answer = System.Windows.MessageBox.Show(this, Loc.F("game_crashed", code),
                AppConfig.LauncherName, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (answer == System.Windows.MessageBoxResult.Yes)
                OpenLogsFolder();
        }
    }

    private static bool IsValidNickname(string nick) => Regex.IsMatch(nick, "^[A-Za-z0-9_]{3,16}$");

    private void NickBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (Settings is null)
            return;
        Settings.Nickname = NickBox.Text.Trim();
        Settings.Save();
        if (StatusText.Text == Loc.T("nick_invalid"))
            ShowIdleStatus();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this) { Owner = this };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void GameFolderButton_Click(object sender, RoutedEventArgs e) => OpenGameFolder();

    public void OpenGameFolder()
    {
        Directory.CreateDirectory(Settings.GameDir);
        Process.Start(new ProcessStartInfo { FileName = Settings.GameDir, UseShellExecute = true });
    }

    public async Task VerifyFilesAsync()
    {
        if (_busy || _gameProcess is not null) return;
        SetBusy(true);
        try
        {
            SetStatus(Loc.T("status_checking"));
            await CheckForUpdatesAsync(fullCheck: true);
            if (_plan is not null)
                await ApplyPlanAsync(_plan);
            SetStatus(_online ? Loc.T("status_done") : Loc.T("status_offline"));
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("err_update"), ex);
            ShowIdleStatus();
        }
        finally
        {
            ShowProgress(false);
            SetBusy(false);
        }
    }

    public enum LauncherUpdateResult { UpToDate, GameRunning, Failed, Installing }

    public async Task<(LauncherUpdateResult Result, Version? Version)> UpdateLauncherAsync(IProgress<double> progress)
    {
        if (_gameProcess is not null)
            return (LauncherUpdateResult.GameRunning, null);

        _release = await _launcherUpdater.CheckAsync();
        RefreshMainButton();
        if (_release is null)
            return (LauncherUpdateResult.UpToDate, null);

        var version = _release.Version;
        return await InstallLauncherUpdateAsync(progress)
            ? (LauncherUpdateResult.Installing, version)
            : (LauncherUpdateResult.Failed, version);
    }

    public async Task OnGameDirChangedAsync()
    {
        SetBusy(true);
        try
        {
            await CheckForUpdatesAsync();
        }
        finally
        {
            SetBusy(false);
            ShowIdleStatus();
        }
    }

    public void ApplyTheme(LauncherTheme mode)
    {
        if (mode != LauncherTheme.System && IsLoaded)
            SystemThemeWatcher.UnWatch(this);

        switch (mode)
        {
            case LauncherTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case LauncherTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                SystemThemeWatcher.Watch(this);
                break;
        }
    }

    public void OpenLogsFolder()
    {
        var logs = Path.Combine(Settings.GameDir, "logs");
        Directory.CreateDirectory(logs);
        Process.Start(new ProcessStartInfo { FileName = logs, UseShellExecute = true });
    }

    private IProgress<(string Text, double Fraction)> CreateProgress() =>
        new Progress<(string Text, double Fraction)>(p =>
        {
            SetStatus(p.Text);
            ProgressBar.Value = p.Fraction;
        });

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshMainButton();
    }

    private void SetStatus(string? text)
    {
        StatusText.Text = text ?? "";
        StatusText.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        var hasStatus = !string.IsNullOrEmpty(text);
        StatusText.Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed;
        VersionText.Visibility = hasStatus ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowProgress(bool visible)
    {
        ProgressBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
            ProgressBar.Value = 0;
    }

    private static void LogError(string title, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "launcher-errors.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}\n{ex}\n\n");
        }
        catch
        {
        }
    }

    public void ShowError(string title, Exception ex)
    {
        LogError(title, ex);
        System.Windows.MessageBox.Show(this, ex.Message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
}
