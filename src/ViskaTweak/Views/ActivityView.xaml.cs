using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ViskaTweak.Models;
using ViskaTweak.Services;

namespace ViskaTweak.Views;

public partial class ActivityView : UserControl
{
    private FiveMWatcher? _watcher;

    public sealed record SessionRow(string When, string Detail);
    public sealed record HistoryRow(string Category, string Action, string When, string Detail, string Target);

    public ActivityView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            LoadSessions();
            LoadHistory();
            if (_watcher is not null) Apply(_watcher.Current);
        };

        JournalService.Changed += LoadHistory;
    }

    /// <summary>The watcher is owned by MainWindow so it keeps running while this page is closed.</summary>
    public void Attach(FiveMWatcher watcher)
    {
        _watcher = watcher;
        watcher.Updated += OnUpdated;
    }

    private void OnUpdated(FiveMWatcher.Status status)
    {
        Apply(status);

        // A finished session only lands in the file once the game exits.
        if (!status.Running) LoadSessions();
    }

    private void Apply(FiveMWatcher.Status status)
    {
        LiveText.Text = status.Running
            ? status.ProcessCount > 1 ? $"Running · {status.ProcessCount} processes" : "Running"
            : "Not running";
        LiveDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            status.Running ? "Brush.Success" : "Brush.TextSecondary");

        if (!status.Running)
        {
            UptimeText.Text = MemoryText.Text = PeakText.Text = CpuText.Text = "—";
            MemoryMeter.Value = 0;
            MemoryNote.Text = "Start FiveM and these fill in. The watcher keeps running while this app is in the tray.";
            return;
        }

        UptimeText.Text = status.Uptime.TotalHours >= 1
            ? $"{(int)status.Uptime.TotalHours}h {status.Uptime.Minutes}m"
            : $"{status.Uptime.Minutes}m {status.Uptime.Seconds}s";

        MemoryText.Text = Format.Bytes(status.MemoryBytes);
        PeakText.Text = Format.Bytes(status.PeakMemoryBytes);
        CpuText.Text = $"{status.CpuPercent:0}%";

        var total = AppState.Hardware?.RamBytes ?? 0;
        if (total > 0)
        {
            var share = status.MemoryBytes * 100.0 / total;
            MemoryMeter.Value = Math.Clamp(share, 0, 100);
            MemoryNote.Text = share > 60
                ? $"{share:0}% of your total system memory — this much pressure is a common cause of stutter."
                : $"{share:0}% of your total system memory.";
        }
    }

    private void LoadSessions()
    {
        var sessions = FiveMWatcher.LoadSessions();

        SessionList.ItemsSource = sessions.Take(8).Select(s => new SessionRow(
            s.Started.ToString("ddd d MMM, HH:mm"),
            $"{Describe(s.Duration)} · peak {Format.Bytes(s.PeakMemoryBytes)}")).ToList();

        NoSessions.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (sessions.Count == 0) { SessionSummary.Text = "—"; return; }

        var week = sessions.Where(s => s.Started > DateTime.Now.AddDays(-7)).ToList();
        var total = TimeSpan.FromTicks(week.Sum(s => s.Duration.Ticks));
        SessionSummary.Text = week.Count == 0
            ? $"{sessions.Count} recorded"
            : $"{week.Count} in the last 7 days · {Describe(total)} played";
    }

    private static string Describe(TimeSpan span)
        => span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m";

    private void LoadHistory()
    {
        var entries = JournalService.All();

        HistoryList.ItemsSource = entries.Take(30).Select(e => new HistoryRow(
            e.Category, e.Action, e.When.ToString("d MMM HH:mm"), e.Detail, e.Target)).ToList();

        NoHistory.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this),
                "Forget the record of changes made to this PC?\n\n"
                + "This only clears the list. Nothing that was changed gets undone — use the Restore "
                + "buttons on the other pages for that.",
                "Clear history", MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        JournalService.Clear();
    }
}
