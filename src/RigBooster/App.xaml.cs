using System.Windows;
using RigBooster.Services;
using RigBooster.Views;

namespace RigBooster;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
}
