using System.IO;
using System.Windows;
using System.Windows.Threading;
using PackLauncher.Services;

namespace PackLauncher;

public partial class App : Application
{
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "launcher-errors.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch
        {
        }

        MessageBox.Show(e.Exception.Message, AppConfig.LauncherName, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
