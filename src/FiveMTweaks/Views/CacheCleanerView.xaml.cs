using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FiveMTweaks.Models;
using FiveMTweaks.Services;

namespace FiveMTweaks.Views;

public partial class CacheCleanerView : UserControl
{
    private List<JunkItem> _items = new();
    private bool _busy;

    public CacheCleanerView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_items.Count > 0 || _busy) return;
            if (AppState.LastScan is { Count: > 0 } cached) Bind(cached);
            else await ScanAsync();
        };
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private async System.Threading.Tasks.Task ScanAsync()
    {
        if (_busy) return;
        SetBusy(true, "Scanning…");
        try
        {
            var progress = new Progress<string>(s => Status(s));
            var items = await CacheScanner.ScanAsync(progress);
            AppState.LastScan = items;
            Bind(items);
            Status(items.Count == 0
                ? "Nothing found — your caches are already clear."
                : $"Found {items.Count} items.");
        }
        catch (Exception ex) { Status($"Scan failed: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    private void Bind(List<JunkItem> items)
    {
        _items = items;
        ItemList.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count == 0) EmptyText.Text = "Nothing found — your caches are already clear.";
        UpdateSelection();
    }

    private void Item_Toggled(object sender, RoutedEventArgs e) => UpdateSelection();

    private void UpdateSelection()
    {
        var selected = _items.Where(i => i.Selected).ToList();
        var total = selected.Sum(i => i.SizeBytes);
        SelectionText.Text = selected.Count == 0
            ? "Nothing selected"
            : $"{selected.Count} selected — {Format.Bytes(total)} to reclaim";
        CleanButton.IsEnabled = !_busy && selected.Count > 0;
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.Selected).ToList();
        if (selected.Count == 0) return;

        // Explicit confirmation naming every folder, with the total, before anything is touched.
        var sb = new StringBuilder();
        sb.AppendLine($"Delete the contents of {selected.Count} folder(s), reclaiming about {Format.Bytes(selected.Sum(i => i.SizeBytes))}?");
        sb.AppendLine();
        foreach (var i in selected.Take(15)) sb.AppendLine($"  • {i.Name} — {i.SizeDisplay}\n    {i.Path}");
        if (selected.Count > 15) sb.AppendLine($"  … and {selected.Count - 15} more.");
        sb.AppendLine();
        sb.AppendLine("This cannot be undone. Close your games and launchers first — files in use are skipped.");

        var answer = MessageBox.Show(Window.GetWindow(this), sb.ToString(), "Confirm cleanup",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) { Status("Cleanup cancelled — nothing was deleted."); return; }

        SetBusy(true, "Cleaning…");
        try
        {
            var report = await CacheScanner.DeleteAsync(selected, new Progress<string>(Status));

            AppState.CachesCleaned = true;
            AppState.FreedBytes += report.FreedBytes;
            AppState.NotifyChanged();

            Bind(_items.Where(i => i.SizeBytes > 0).ToList());
            AppState.LastScan = _items;

            Status(report.Skipped.Count == 0
                ? $"Freed {Format.Bytes(report.FreedBytes)} across {report.FilesDeleted} files."
                : $"Freed {Format.Bytes(report.FreedBytes)} across {report.FilesDeleted} files. {report.Skipped.Count} were locked and left alone.");
        }
        catch (Exception ex) { Status($"Cleanup failed: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        ScanButton.IsEnabled = !busy;
        CleanButton.IsEnabled = !busy && _items.Any(i => i.Selected);
        ItemList.IsEnabled = !busy;
        if (status is not null) Status(status);
    }

    private void Status(string text)
    {
        StatusText.Text = text;
        StatusBox.Visibility = Visibility.Visible;
    }
}
