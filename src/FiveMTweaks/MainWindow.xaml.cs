using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FiveMTweaks.Services;
using FiveMTweaks.Views;

namespace FiveMTweaks;

public partial class MainWindow : Window
{
    private readonly DashboardView _dashboard = new();
    private readonly CacheCleanerView _cache = new();
    private readonly GameOptimizerView _optimizer = new();
    private readonly BoostView _boost = new();
    private readonly SettingsView _settings = new();

    public MainWindow()
    {
        InitializeComponent();

        LicenseFooter.Text = $"Activated — {LicenseService.ActivatedUser}";
        _settings.Deactivated += () => { Close(); };
        _dashboard.NavigateToCache += () => Nav.SelectedIndex = 1;

        ThemeService.EffectsChanged += ApplyEffectPreference;
        Closed += (_, _) => ThemeService.EffectsChanged -= ApplyEffectPreference;
        ApplyEffectPreference();

        // Selects the first page, which raises Nav_SelectionChanged and fills Host. Done here
        // rather than as SelectedIndex="0" in XAML, where it fires mid-InitializeComponent.
        Nav.SelectedIndex = 0;
    }

    private void ApplyEffectPreference()
        => CursorGlow.Visibility = ThemeService.EffectsAllowed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Moves the radial glow to follow the pointer.</summary>
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!ThemeService.EffectsAllowed || ActualWidth <= 0 || ActualHeight <= 0) return;

        var p = e.GetPosition(this);
        var point = new Point(p.X / ActualWidth, p.Y / ActualHeight);
        GlowBrush.Center = point;
        GlowBrush.GradientOrigin = point;
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ListBox raises this while its own XAML is still being parsed, before the rest of the
        // window exists. Nothing to switch to until Host has been created.
        if (Host is null) return;

        Host.Content = Nav.SelectedIndex switch
        {
            1 => _cache,
            2 => _optimizer,
            3 => _boost,
            4 => _settings,
            _ => _dashboard
        };

        AnimatePageIn();

        // Move focus into the page so keyboard and screen-reader users land where they expect.
        var page = Host.Content as UIElement;
        Dispatcher.InvokeAsync(() => page?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    /// <summary>Short fade and rise as a page swaps in. Skipped entirely when effects are off.</summary>
    private void AnimatePageIn()
    {
        if (!ThemeService.EffectsAllowed)
        {
            Host.Opacity = 1;
            HostShift.Y = 0;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);

        Host.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        HostShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, duration) { EasingFunction = ease });
    }

    private void GoToPage_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string s && int.TryParse(s, out var i) && i >= 0 && i < Nav.Items.Count)
            Nav.SelectedIndex = i;
    }
}
