using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PackLauncher.Services;

namespace PackLauncher;

public partial class SettingsWindow
{
    private readonly MainWindow _main;
    private readonly LauncherSettings _settings;
    private bool _uiLoading = true;

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        _settings = main.Settings;

        InitializeComponent();

        foreach (var (_, name) in Loc.Languages)
            LanguageCombo.Items.Add(name);

        LoadIntoUi();
        _uiLoading = false;

        Loc.I.LanguageChanged += RefreshTexts;
        Closed += (_, _) => Loc.I.LanguageChanged -= RefreshTexts;
    }

    private void LoadIntoUi()
    {
        LanguageCombo.SelectedIndex = Math.Max(0, Array.FindIndex(Loc.Languages, l => l.Code == Loc.I.Language));

        var profile = RamPlanner.EffectiveProfile(_settings).ToString();
        foreach (var radio in RamRadios())
            if ((string)radio.Tag == profile)
                radio.IsChecked = true;

        WidthBox.Text = _settings.WindowWidth.ToString();
        HeightBox.Text = _settings.WindowHeight.ToString();
        FullScreenSwitch.IsChecked = _settings.FullScreen;
        UpdateSizeBoxes();
        AfterLaunchCombo.SelectedIndex = (int)_settings.AfterLaunch;
        ThemeCombo.SelectedIndex = (int)_settings.Theme;
        GameDirText.Text = _settings.GameDir;

        RefreshTexts();
    }

    private void RefreshTexts()
    {
        var gb = Loc.T("gb");
        var detected = RamPlanner.DetectedProfile();
        foreach (var radio in RamRadios())
        {
            var value = int.Parse((string)radio.Tag);
            var text = value == 32 ? Loc.T("s_ram_32") : $"{value} {gb}";
            radio.Content = value == detected ? $"{text}  ·  {Loc.T("s_yours")}" : text;
        }

        RefreshRamTexts();

        AboutVersionText.Text = $"{AppConfig.LauncherName} · {Loc.F("version", AppPaths.CurrentVersionText)}";
        SetAboutStatus(null);

        ReselectCombo(AfterLaunchCombo);
        ReselectCombo(ThemeCombo);
    }

    private void ReselectCombo(ComboBox combo)
    {
        var wasLoading = _uiLoading;
        _uiLoading = true;
        var index = combo.SelectedIndex;
        combo.SelectedIndex = -1;
        combo.SelectedIndex = index;
        _uiLoading = wasLoading;
    }

    private IEnumerable<RadioButton> RamRadios() => [Ram4, Ram8, Ram16, Ram32];

    private void RefreshRamTexts()
    {
        var totalGb = RamPlanner.DetectTotalRamMb() / 1024.0;
        RamSummaryText.Text = Loc.F("s_ram_summary", totalGb.ToString("0.#"), RamPlanner.FormatGb(RamPlanner.GetMaxHeapMb(_settings)));
        MemoryArgsBox.Text = RamPlanner.MemoryArgs(_settings);

        var wasLoading = _uiLoading;
        _uiLoading = true;
        if (_settings.AutoJvmFlags || string.IsNullOrWhiteSpace(_settings.CustomJvmFlags))
            FlagsBox.Text = RamPlanner.FlagsAsText(RamPlanner.DefaultJvmFlags(RamPlanner.GetMaxHeapMb(_settings)));
        else
            FlagsBox.Text = _settings.CustomJvmFlags;
        _uiLoading = wasLoading;
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_uiLoading || LanguageCombo.SelectedIndex < 0) return;
        var code = Loc.Languages[LanguageCombo.SelectedIndex].Code;
        _settings.Language = code;
        _settings.Save();
        Loc.I.SetLanguage(code);
    }

    private void Ram_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiLoading || sender is not RadioButton { Tag: string tag })
            return;

        _settings.PcRamGb = int.Parse(tag);
        _settings.Save();
        RefreshRamTexts();
    }

    private void FlagsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_uiLoading) return;

        var text = FlagsBox.Text.Trim();
        var auto = RamPlanner.FlagsAsText(RamPlanner.DefaultJvmFlags(RamPlanner.GetMaxHeapMb(_settings)));
        var isAuto = text.Length == 0 || NormalizeFlags(text) == NormalizeFlags(auto);

        _settings.AutoJvmFlags = isAuto;
        _settings.CustomJvmFlags = isAuto ? "" : text;
        _settings.Save();
    }

    private static string NormalizeFlags(string text) => string.Join(' ', RamPlanner.SplitArgs(text));

    private void ResetFlags_Click(object sender, RoutedEventArgs e)
    {
        _settings.CustomJvmFlags = "";
        _settings.AutoJvmFlags = true;
        _settings.Save();
        RefreshRamTexts();
    }

    private void DigitsOnly(object sender, System.Windows.Input.TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void Resolution_Changed(object sender, TextChangedEventArgs e)
    {
        if (_uiLoading) return;
        if (int.TryParse(WidthBox.Text, out var w) && w >= 640) _settings.WindowWidth = Math.Min(w, 16384);
        if (int.TryParse(HeightBox.Text, out var h) && h >= 480) _settings.WindowHeight = Math.Min(h, 16384);
        _settings.Save();
    }

    private void Resolution_LostFocus(object sender, RoutedEventArgs e)
    {
        _uiLoading = true;
        WidthBox.Text = _settings.WindowWidth.ToString();
        HeightBox.Text = _settings.WindowHeight.ToString();
        _uiLoading = false;
    }

    private void UpdateSizeBoxes()
    {
        WidthBox.IsEnabled = !_settings.FullScreen;
        HeightBox.IsEnabled = !_settings.FullScreen;
    }

    private void FullScreen_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiLoading) return;
        _settings.FullScreen = FullScreenSwitch.IsChecked == true;
        _settings.Save();
        UpdateSizeBoxes();
    }

    private void AfterLaunch_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_uiLoading || AfterLaunchCombo.SelectedIndex < 0) return;
        _settings.AfterLaunch = (AfterLaunchAction)AfterLaunchCombo.SelectedIndex;
        _settings.Save();
    }

    private void OpenGameDir_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_settings.GameDir);
        Process.Start(new ProcessStartInfo { FileName = _settings.GameDir, UseShellExecute = true });
    }

    private async void ChangeGameDir_Click(object sender, RoutedEventArgs e)
    {
        if (_main.IsBusy)
            return;

        Directory.CreateDirectory(_settings.GameDir);
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("s_folder_dialog"),
            InitialDirectory = _settings.GameDir
        };
        if (dialog.ShowDialog(this) != true)
            return;

        if (string.Equals(Path.GetFullPath(dialog.FolderName), Path.GetFullPath(_settings.GameDir), StringComparison.OrdinalIgnoreCase))
            return;

        _settings.GameDirectory = dialog.FolderName;
        _settings.Save();
        GameDirText.Text = _settings.GameDir;
        await _main.OnGameDirChangedAsync();
    }

    private async void Verify_Click(object sender, RoutedEventArgs e) => await _main.VerifyFilesAsync();

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => _main.OpenLogsFolder();

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_uiLoading || ThemeCombo.SelectedIndex < 0) return;
        _settings.Theme = (LauncherTheme)ThemeCombo.SelectedIndex;
        _settings.Save();
        _main.ApplyTheme(_settings.Theme);
    }

    private async void UpdateLauncher_Click(object sender, RoutedEventArgs e)
    {
        UpdateLauncherButton.IsEnabled = false;
        SetAboutStatus(Loc.T("s_checking"));
        try
        {
            var progress = new Progress<double>(p =>
            {
                UpdateProgressBar.Visibility = Visibility.Visible;
                UpdateProgressBar.Value = p;
                SetAboutStatus(Loc.F("status_launcher_download", p));
            });

            var (result, version) = await _main.UpdateLauncherAsync(progress);
            switch (result)
            {
                case MainWindow.LauncherUpdateResult.UpToDate:
                    SetAboutStatus(Loc.F("s_up_to_date_version", AppPaths.CurrentVersionText));
                    break;
                case MainWindow.LauncherUpdateResult.GameRunning:
                    SetAboutStatus(Loc.T("s_close_game_first"));
                    break;
                case MainWindow.LauncherUpdateResult.Installing:
                    SetAboutStatus(Loc.F("s_installing_version", version?.ToString(3) ?? ""));
                    return;
                default:
                    SetAboutStatus(Loc.T("err_update"));
                    break;
            }
        }
        catch (Exception ex)
        {
            SetAboutStatus(Loc.T("err_update") + ": " + ex.Message);
        }

        UpdateProgressBar.Visibility = Visibility.Collapsed;
        UpdateLauncherButton.IsEnabled = true;
    }

    private void SetAboutStatus(string? text)
    {
        AboutUpdateText.Text = text ?? "";
        AboutUpdateText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
