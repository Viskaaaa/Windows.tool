using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using RigBooster.Services;
using RigBooster.Views;

namespace RigBooster;

public partial class App : Application
{
    private static readonly string CrashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RigBooster", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Without these, any unhandled exception closes the window with no explanation at all.
        DispatcherUnhandledException += (_, args) =>
        {
            Report(args.Exception, "UI thread");
            args.Handled = true;      // keep the app alive so the user can read the message
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report(args.ExceptionObject as Exception, "background thread");

        try
        {
            ThemeService.Load();
            ThemeService.Apply(ThemeService.Theme, ThemeService.FontScale);

            // License gate: nothing else opens until this passes.
            if (!LicenseService.TryRestoreActivation())
            {
                var gate = new LicenseWindow();
                if (gate.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            Report(ex, "startup");
            Shutdown();
        }
    }

    private static void Report(Exception? ex, string where)
    {
        if (ex is null) return;

        var detail = $"[{DateTime.Now:u}] {where}\n{ex}\n\n";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
            File.AppendAllText(CrashLog, detail);
        }
        catch { /* logging must never be the thing that crashes */ }

        MessageBox.Show(
            $"Rig Booster hit an error on the {where}.\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
            $"The full details were written to:\n{CrashLog}",
            "Rig Booster error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
