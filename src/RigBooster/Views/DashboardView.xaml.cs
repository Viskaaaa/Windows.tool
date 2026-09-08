using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RigBooster.Models;
using RigBooster.Services;

namespace RigBooster.Views;

public partial class DashboardView : UserControl
{
    private bool _loaded;

    /// <summary>Raised by "Review and clean" — MainWindow switches to the cleaner tab.</summary>
    public event Action? NavigateToCache;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        AppState.Changed += RefreshFps;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        var hw = AppState.Hardware ??= await HardwareService.DetectAsync();

        GpuText.Text = hw.GpuName;
        VramText.Text = hw.GpuVramBytes > 0 ? $"{hw.VramDisplay} VRAM" : "VRAM not reported";
        RamText.Text = hw.RamDisplay;
        CpuText.Text = hw.CpuDisplay;
        CpuNameText.Text = hw.CpuName;

        TierText.Text = hw.TierDisplay;
        var (fg, bg) = hw.Tier switch
        {
            PcTier.Low => ("Brush.Warning", "Brush.WarningBg"),
            PcTier.Medium => ("Brush.Text", "Brush.CardAlt"),
            _ => ("Brush.Success", "Brush.SuccessBg")
        };
        TierText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, fg);
        TierPill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, bg);

        RefreshFps();
        await ScanSummaryAsync();
    }

    private void RefreshFps()
    {
        if (AppState.Hardware is not { } hw) return;

        var est = FpsEstimator.Estimate(hw, AppState.PresetApplied, AppState.CachesCleaned, AppState.FreedBytes);
        BeforeText.Text = est.Before.ToString();

        if (!AppState.PresetApplied && !AppState.CachesCleaned)
        {
            AfterText.Text = "—";
            GainText.Text = "not applied";
            FpsNote.Text = "Apply a preset in Game optimizer to see the projected after figure.";
            return;
        }

        AfterText.Text = est.After.ToString();
        GainText.Text = $"+{est.PercentGain}%";
        FpsNote.Text = "Estimated, not measured. Real gains depend on the server population and what else is running.";
    }

    private async System.Threading.Tasks.Task ScanSummaryAsync()
    {
        try
        {
            var items = AppState.LastScan ?? await CacheScanner.ScanAsync(null);
            AppState.LastScan = items;

            JunkList.ItemsSource = items.Take(4).ToList();
            var total = items.Sum(i => i.SizeBytes);
            JunkTotal.Text = total > 0 ? $"{Format.Bytes(total)} reclaimable" : "Nothing found";
            ReviewButton.IsEnabled = total > 0;
            if (total == 0) ReviewButton.Content = "Nothing to clean";
        }
        catch (Exception ex)
        {
            JunkTotal.Text = "Scan failed";
            JunkList.ItemsSource = new[] { new JunkItem { Name = ex.Message } };
        }
    }

    private void Review_Click(object sender, RoutedEventArgs e) => NavigateToCache?.Invoke();
}
