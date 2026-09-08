using System;
using System.Windows;
using System.Windows.Controls;
using RigBooster.Services;

namespace RigBooster.Views;

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

    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this),
                "Forget the license key on this PC and close Rig Booster?",
                "Confirm deactivation", MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        LicenseService.Deactivate();
        Deactivated?.Invoke();
    }
}
