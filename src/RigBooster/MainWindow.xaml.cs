using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RigBooster.Services;
using RigBooster.Views;

namespace RigBooster;

public partial class MainWindow : Window
{
    private readonly DashboardView _dashboard = new();
    private readonly CacheCleanerView _cache = new();
    private readonly GameOptimizerView _optimizer = new();
    private readonly SettingsView _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        LicenseFooter.Text = $"Activated — {LicenseService.ActivatedUser}";
        _settings.Deactivated += () => { Close(); };
        _dashboard.NavigateToCache += () => Nav.SelectedIndex = 1;

        // Selects the first page, which raises Nav_SelectionChanged and fills Host. Done here
        // rather than as SelectedIndex="0" in XAML, where it fires mid-InitializeComponent.
        Nav.SelectedIndex = 0;
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
            3 => _settings,
            _ => _dashboard
        };

        // Move focus into the page so keyboard and screen-reader users land where they expect.
        var page = Host.Content as UIElement;
        Dispatcher.InvokeAsync(() => page?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    private void GoToPage_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string s && int.TryParse(s, out var i) && i >= 0 && i < Nav.Items.Count)
            Nav.SelectedIndex = i;
    }
}
