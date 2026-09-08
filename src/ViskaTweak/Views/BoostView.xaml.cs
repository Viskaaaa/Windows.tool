using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ViskaTweak.Models;
using ViskaTweak.Services;

namespace ViskaTweak.Views;

public partial class BoostView : UserControl
{
    private ReShadeService.Install? _reshade;
    private bool _loaded;

    public BoostView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            Refresh();
        };
    }

    private void Refresh()
    {
        var exe = BoostService.FindFiveMExe();
        FiveMPath.Text = exe ?? "FiveM.exe not found — install or launch FiveM once, then reopen this page.";

        LoadStartupApps();

        _reshade = ReShadeService.Detect();
        if (_reshade is null)
        {
            ReShadeState.Text = "Not installed";
            ReShadeState.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            ReShadePath.Text = "No ReShade found in your FiveM folder. Nothing to tune — which is already the fastest setting.";
            ReShadeButtons.IsEnabled = false;
            return;
        }

        ReShadeButtons.IsEnabled = true;
        ReShadePath.Text = _reshade.DisplayFolder;
        ReShadeState.Text = _reshade.Enabled ? "Active" : "Switched off";
        ReShadeState.SetResourceReference(TextBlock.ForegroundProperty,
            _reshade.Enabled ? "Brush.Warning" : "Brush.Success");
        ToggleButton.Content = _reshade.Enabled ? "Turn ReShade off" : "Turn ReShade on";

        var state = ReShadeService.ReadState(_reshade);
        PerfState.Text = state.PerformanceMode ? "On" : "Off";
        PerfState.SetResourceReference(TextBlock.ForegroundProperty,
            state.PerformanceMode ? "Brush.Success" : "Brush.TextSecondary");

        EffectState.Text = state.EnabledEffects < 0 ? "preset not found" : state.EnabledEffects.ToString();
        PresetState.Text = string.IsNullOrEmpty(state.PresetName) ? "none" : state.PresetName;
    }

    private void LoadStartupApps()
    {
        var apps = BoostService.ListStartupApps();
        StartupList.ItemsSource = apps;
        NoStartup.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var on = apps.Count(a => a.Enabled);
        StartupSummary.Text = apps.Count == 0 ? "none" : $"{on} of {apps.Count} enabled";
    }

    private void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.Tag is not BoostService.StartupApp app) return;

        var wanted = box.IsChecked == true;
        if (wanted == app.Enabled) return;      // fired while the list was being bound

        Show(BoostService.SetStartupApp(app, wanted));
        LoadStartupApps();
    }

    private void Effects_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Turn off Windows desktop animations, window shadows and transparency?\n\n"
                     + "Windows will look plainer. Undo puts the defaults back."))
            return;
        Show(BoostService.TrimWindowsEffects());
    }

    private void EffectsUndo_Click(object sender, RoutedEventArgs e)
        => Show(BoostService.RestoreWindowsEffects());

    private void Fso_Click(object sender, RoutedEventArgs e) => Show(BoostService.DisableFullscreenOptimisations());

    private void Gpu_Click(object sender, RoutedEventArgs e) => Show(BoostService.PreferHighPerformanceGpu());

    private void Boost_Click(object sender, RoutedEventArgs e) => Show(BoostService.BoostRunningProcess());

    private void UndoFiveM_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Undo the FiveM tweaks?\n\nFullscreen optimisations and the GPU preference go back to the Windows defaults."))
            return;
        Show(BoostService.RestoreAll());
    }

    private void Perf_Click(object sender, RoutedEventArgs e)
    {
        if (_reshade is null) return;
        Show(ReShadeService.EnablePerformanceMode(_reshade));
        Refresh();
    }

    private void Strip_Click(object sender, RoutedEventArgs e)
    {
        if (_reshade is null) return;
        if (!Confirm("Remove the expensive effects from your ReShade preset?\n\n"
                     + "Ray tracing, ambient occlusion, bloom, depth of field and similar are dropped. "
                     + "The preset is backed up first, and Restore puts them all back."))
            return;
        Show(ReShadeService.StripHeavyEffects(_reshade));
        Refresh();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_reshade is null) return;

        var turningOff = _reshade.Enabled;
        if (!Confirm(turningOff
                ? "Switch ReShade off?\n\nThe loader DLL is renamed, not deleted, so this is reversible. Close FiveM first."
                : "Switch ReShade back on?\n\nClose FiveM first."))
            return;

        Show(ReShadeService.SetEnabled(_reshade, !turningOff));
        Refresh();
    }

    private void RestoreReShade_Click(object sender, RoutedEventArgs e)
    {
        if (_reshade is null) return;
        Show(ReShadeService.Restore(_reshade));
        Refresh();
    }

    private bool Confirm(string message)
        => MessageBox.Show(Window.GetWindow(this), message, "Confirm",
               MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private void Show(TweakResult result)
    {
        StatusText.Text = result.Message;
        StatusBox.Visibility = Visibility.Visible;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty,
            result.Ok ? "Brush.Text" : "Brush.Danger");

        if (result.Ok && result.Changed > 0)
        {
            AppState.PresetApplied = true;
            AppState.NotifyChanged();
        }
    }
}
