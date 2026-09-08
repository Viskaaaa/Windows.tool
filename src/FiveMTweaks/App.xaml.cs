using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FiveMTweaks.Services;
using FiveMTweaks.Views;

namespace FiveMTweaks;

public partial class App : Application
{
    private static readonly string CrashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FiveMTweaks", "crash.log");

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

        WatchInputMode();

        try
        {
            ThemeService.Load();
            ThemeService.Apply(ThemeService.Theme, ThemeService.FontScale);
            BackgroundPrefs.Load();

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

            // Launched by the Windows startup entry: create the window so the watcher and tray are
            // live, but leave it hidden rather than interrupting whatever the user is doing.
            var startHidden = e.Args.Any(a =>
                string.Equals(a, BackgroundPrefs.TrayArgument, StringComparison.OrdinalIgnoreCase));

            if (!startHidden) main.Show();
        }
        catch (Exception ex)
        {
            Report(ex, "startup");
            Shutdown();
        }
    }

    /// <summary>
    /// Focus outlines belong to keyboard users. Showing them on a mouse click reads as a rendering
    /// glitch, so the ring only appears once someone actually navigates with the keyboard.
    /// </summary>
    private static void WatchInputMode()
    {
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler((_, e) =>
            {
                if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down)
                    ShowFocusRing(true);
            }));

        EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler((_, _) => ShowFocusRing(false)));
    }

    private static void ShowFocusRing(bool visible)
    {
        var key = visible ? "FocusRing.Visible" : "FocusRing.None";
        if (Current.TryFindResource(key) is Style style)
            Current.Resources["FocusRing"] = style;
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
            $"FiveM Tweaks hit an error on the {where}.\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
            $"The full details were written to:\n{CrashLog}",
            "FiveM Tweaks error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
