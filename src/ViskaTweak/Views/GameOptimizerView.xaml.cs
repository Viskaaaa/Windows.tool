using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ViskaTweak.Models;
using ViskaTweak.Services;

namespace ViskaTweak.Views;

public partial class GameOptimizerView : UserControl
{
    private List<GameTarget> _games = new();
    private bool _loaded;

    public GameOptimizerView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;

            if (AppState.Hardware is { } hw)
            {
                TierHint.Text = $"Your PC reads as {hw.TierDisplay.ToLowerInvariant()}. Override it if you disagree.";

                // Weak hardware defaults to Stability, not Low: on these machines the complaint is
                // almost always stutter rather than a low average.
                (hw.Tier switch
                {
                    PcTier.Medium => MediumRadio,
                    PcTier.High => HighRadio,
                    _ => StabilityRadio
                }).IsChecked = true;
            }
            LoadGames();
        };
    }

    private PresetProfile SelectedProfile =>
        MediumRadio.IsChecked == true ? PresetProfile.Medium :
        HighRadio.IsChecked == true ? PresetProfile.High :
        LowRadio.IsChecked == true ? PresetProfile.Low :
        PotatoRadio.IsChecked == true ? PresetProfile.Potato : PresetProfile.Stability;

    private void LoadGames()
    {
        _games = OptimizerService.DetectGames();
        GameList.ItemsSource = _games;
        NoGamesText.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        LoadGames();
        Status(_games.Count == 0 ? "No settings files found." : $"Found {_games.Count}.");
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameTarget game) return;

        var sb = new StringBuilder();
        sb.AppendLine($"Apply the {SelectedProfile} preset to {game.DisplayName}?");
        sb.AppendLine();
        sb.AppendLine(game.SettingsPath);
        sb.AppendLine();
        sb.AppendLine(game.HasBackup
            ? "A backup from an earlier run already exists and will be kept — Restore still returns the original file."
            : "The current file is copied to a .bak first, so Restore can undo this.");
        sb.AppendLine();
        sb.AppendLine("Close the game before continuing, or it will overwrite these values on exit.");

        if (MessageBox.Show(Window.GetWindow(this), sb.ToString(), "Confirm preset",
                MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK)
        {
            Status("Cancelled — nothing was changed.");
            return;
        }

        var result = OptimizerService.ApplyProfile(game, SelectedProfile);
        Status(result.Message);

        if (result.Ok && result.Changed > 0)
        {
            AppState.PresetApplied = true;
            AppState.StabilityMode = SelectedProfile == PresetProfile.Stability;
            AppState.NotifyChanged();
        }
        LoadGames();
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameTarget game) return;

        if (MessageBox.Show(Window.GetWindow(this),
                $"Put back the original settings for {game.DisplayName}?\n\n{game.SettingsPath}",
                "Confirm restore", MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        var result = OptimizerService.Restore(game);
        Status(result.Message);

        if (result.Ok)
        {
            AppState.PresetApplied = false;
            AppState.NotifyChanged();
        }
    }

    private void ApplyTweaks_Click(object sender, RoutedEventArgs e)
    {
        var wanted = new List<string>();
        if (PowerCheck.IsChecked == true) wanted.Add("• Power plan → High performance");
        if (GameModeCheck.IsChecked == true) wanted.Add("• Windows Game Mode → on");
        if (DvrCheck.IsChecked == true) wanted.Add("• Game DVR → off");

        if (wanted.Count == 0) { Status("Tick at least one tweak first."); return; }

        if (MessageBox.Show(Window.GetWindow(this),
                "Apply these changes to Windows?\n\n" + string.Join("\n", wanted) +
                "\n\nAll three are reversible from the Undo tweaks button.",
                "Confirm Windows tweaks", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
        {
            Status("Cancelled — Windows was not changed.");
            return;
        }

        var messages = new List<string>();
        if (PowerCheck.IsChecked == true) messages.Add(OptimizerService.EnableHighPerformancePowerPlan().Message);
        if (GameModeCheck.IsChecked == true) messages.Add(OptimizerService.EnableGameMode().Message);
        if (DvrCheck.IsChecked == true) messages.Add(OptimizerService.DisableGameDvr().Message);
        Status(string.Join(" ", messages));
    }

    private void UndoTweaks_Click(object sender, RoutedEventArgs e)
        => Status(OptimizerService.RestoreWindowsTweaks().Message);

    private void Status(string text)
    {
        StatusText.Text = text;
        StatusBox.Visibility = Visibility.Visible;
    }
}
