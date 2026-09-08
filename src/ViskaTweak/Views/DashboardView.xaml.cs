using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using ViskaTweak.Controls;
using ViskaTweak.Models;
using ViskaTweak.Services;

namespace ViskaTweak.Views;

public partial class DashboardView : UserControl
{
    /// <summary>Full deflection on the FPS arc. 120 covers anything this tool is aimed at.</summary>
    private const double GaugeMax = 120;

    private readonly SystemMonitor _monitor = new(TimeSpan.FromSeconds(1));
    private bool _loaded;

    /// <summary>Raised by "Review and clean" — MainWindow switches to the cleaner tab.</summary>
    public event Action? NavigateToCache;

    /// <summary>One bar in the junk breakdown.</summary>
    public sealed record CategoryBar(string Label, string SizeDisplay, double Percent);

    public DashboardView()
    {
        InitializeComponent();

        _monitor.Sampled += OnSampled;
        Loaded += OnLoaded;
        Unloaded += (_, _) => _monitor.Stop();   // no point sampling a page nobody is looking at
        AppState.Changed += RefreshFps;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _monitor.Start();

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
        TierText.SetResourceReference(TextBlock.ForegroundProperty, fg);
        TierPill.SetResourceReference(Border.BackgroundProperty, bg);

        RefreshFps();
        await ScanSummaryAsync();
    }

    private void OnSampled(double cpu, double ramPercent, long ramUsed)
    {
        CpuLoadText.Text = $"{cpu:0}% in use";
        RamLoadText.Text = $"{Format.Bytes(ramUsed)} in use";
        AnimateTo(CpuMeter, cpu);
        AnimateTo(RamMeter, ramPercent);
    }

    private static void AnimateTo(ProgressBar bar, double value)
    {
        if (!ThemeService.EffectsAllowed)
        {
            bar.BeginAnimation(RangeBase.ValueProperty, null);
            bar.Value = value;
            return;
        }

        bar.BeginAnimation(RangeBase.ValueProperty,
            new DoubleAnimation(value, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void RefreshFps()
    {
        if (AppState.Hardware is not { } hw) return;

        var est = FpsEstimator.Estimate(hw, AppState.PresetApplied, AppState.CachesCleaned,
            AppState.FreedBytes, AppState.StabilityMode);
        var applied = AppState.PresetApplied || AppState.CachesCleaned;
        var shown = applied ? est.After : est.Before;

        BeforeText.Text = est.Before.ToString();
        AfterText.Text = shown.ToString();
        AfterText.SetResourceReference(TextBlock.ForegroundProperty,
            applied ? "Brush.Success" : "Brush.TextSecondary");

        GainText.Text = applied ? $"+{est.PercentGain}%" : "baseline";
        FpsNote.Text = applied
            ? AppState.StabilityMode
                ? "Stability profile: a lower average than the Low preset, held far more consistently. Smoothness does not show up in this number."
                : "Estimated, not measured. Real gains depend on the server population and what else is running."
            : "Apply a preset in Game optimizer to see the projected figure.";

        var fraction = Math.Clamp(shown / GaugeMax, 0, 1);
        if (ThemeService.EffectsAllowed)
            FpsGauge.BeginAnimation(ArcGauge.ValueProperty,
                new DoubleAnimation(fraction, TimeSpan.FromMilliseconds(900))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        else
        {
            FpsGauge.BeginAnimation(ArcGauge.ValueProperty, null);
            FpsGauge.Value = fraction;
        }
    }

    private async System.Threading.Tasks.Task ScanSummaryAsync()
    {
        try
        {
            var items = AppState.LastScan ?? await CacheScanner.ScanAsync(null);
            AppState.LastScan = items;

            var total = items.Sum(i => i.SizeBytes);
            JunkTotal.Text = total > 0 ? $"{Format.Bytes(total)} reclaimable" : "Nothing found";
            JunkChart.ItemsSource = BuildChart(items);

            ReviewButton.IsEnabled = total > 0;
            if (total == 0) ReviewButton.Content = "Nothing to clean";
        }
        catch (Exception ex)
        {
            JunkTotal.Text = "Scan failed";
            JunkChart.ItemsSource = new[] { new CategoryBar(ex.Message, "", 0) };
        }
    }

    /// <summary>Groups the scan by category and scales each bar against the largest one.</summary>
    private static List<CategoryBar> BuildChart(List<JunkItem> items)
    {
        var groups = items
            .GroupBy(i => i.Category)
            .Select(g => (Label: g.Key, Bytes: g.Sum(i => i.SizeBytes)))
            .Where(g => g.Bytes > 0)
            .OrderByDescending(g => g.Bytes)
            .Take(6)
            .ToList();

        if (groups.Count == 0) return new List<CategoryBar>();

        var largest = groups[0].Bytes;
        return groups
            .Select(g => new CategoryBar(g.Label, Format.Bytes(g.Bytes), g.Bytes * 100.0 / largest))
            .ToList();
    }

    private void Review_Click(object sender, RoutedEventArgs e) => NavigateToCache?.Invoke();
}
