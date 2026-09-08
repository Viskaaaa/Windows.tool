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
