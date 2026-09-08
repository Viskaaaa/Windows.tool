using System;
using System.Windows;
using System.Windows.Controls;
using FiveMTweaks.Services;

namespace FiveMTweaks.Views;

public partial class SettingsView : UserControl
{
    private bool _ready;

    /// <summary>Raised after the key is forgotten; MainWindow closes the app.</summary>
    public event Action? Deactivated;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_ready) return;
            LicenseStatus.Text = $"Activated — {LicenseService.ActivatedUser}";
            RedRadio.IsChecked = ThemeService.Theme == AppTheme.Red;
            ContrastRadio.IsChecked = ThemeService.Theme == AppTheme.HighContrast;
            ScaleSlider.Value = ThemeService.FontScale;
            EffectsCheck.IsChecked = ThemeService.Effects;
            KeepRunningCheck.IsChecked = BackgroundPrefs.KeepRunning;
            NotifyCheck.IsChecked = BackgroundPrefs.NotifyOnLaunch;
            StartupCheck.IsChecked = BackgroundPrefs.StartsWithWindows;
            RefreshInstall();
            ScaleLabel.Text = $"Text size: {ThemeService.FontScale * 100:0}%";
            _ready = true;
        };
    }

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ThemeService.Apply(ContrastRadio.IsChecked == true ? AppTheme.HighContrast : AppTheme.Red, ScaleSlider.Value);
    }

    private void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        ScaleLabel.Text = $"Text size: {e.NewValue * 100:0}%";
        ThemeService.Apply(ThemeService.Theme, e.NewValue);
    }

    private void Effects_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ThemeService.SetEffects(EffectsCheck.IsChecked == true);
    }

    private void RefreshInstall()
    {
        var installed = InstallService.IsInstalled;

        InstallState.Text = InstallService.RunningFromInstall ? "Installed" : installed ? "Installed elsewhere" : "Not installed";
        InstallState.SetResourceReference(TextBlock.ForegroundProperty,
            installed ? "Brush.Success" : "Brush.TextSecondary");

        InstallPath.Text = InstallService.InstallFolder;
        InstallButton.Content = installed ? "Reinstall / update" : "Install";
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var result = InstallService.Install(DesktopCheck.IsChecked == true);
        ShowInstall(result.Ok, result.Message);
        RefreshInstall();
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this),
                "Remove the shortcuts and the Add or Remove Programs entry?\n\n"
                + "Your settings, licence and history are left alone. The app folder has to be "
                + "deleted by hand afterwards, because a running program cannot delete itself.",
                "Uninstall", MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        var result = InstallService.Uninstall();
        ShowInstall(result.Ok, result.Message);
        RefreshInstall();
    }

    private void ShowInstall(bool ok, string message)
    {
        InstallStatusText.Text = message;
        InstallStatusBox.Visibility = Visibility.Visible;
        InstallStatusText.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Brush.Text" : "Brush.Danger");
    }

    private void Background_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        BackgroundPrefs.Set(KeepRunningCheck.IsChecked == true, NotifyCheck.IsChecked == true);
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        var wanted = StartupCheck.IsChecked == true;
        if (BackgroundPrefs.SetStartWithWindows(wanted)) return;

        // Put the box back rather than showing a tick for something that did not happen.
        _ready = false;
        StartupCheck.IsChecked = !wanted;
        _ready = true;
        MessageBox.Show(Window.GetWindow(this),
            "Windows would not let the startup entry be changed.",
            "Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this),
                "Forget the license key on this PC and close FiveM Tweaks?",
                "Confirm deactivation", MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        LicenseService.Deactivate();
        Deactivated?.Invoke();
    }
}
