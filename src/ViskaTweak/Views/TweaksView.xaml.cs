using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ViskaTweak.Models;
using ViskaTweak.Services;

namespace ViskaTweak.Views;

public partial class TweaksView : UserControl
{
    /// <summary>View wrapper: the catalog stays free of WPF types.</summary>
    public sealed class Card
    {
        public required TweakCatalog.Tweak Tweak { get; init; }

        public string Icon => Tweak.Icon;
        public string Name => Tweak.Name;
        public string Description => Tweak.Description;

        public Visibility AdminBadge => Tweak.NeedsAdmin ? Visibility.Visible : Visibility.Collapsed;
        public Visibility UndoVisibility => Tweak.Undo is null ? Visibility.Collapsed : Visibility.Visible;

        // Spelled out for screen readers, which would otherwise hear eight identical "Apply tweak".
        public string ApplyLabel => $"Apply {Tweak.Name}"
                                    + (Tweak.NeedsAdmin ? ", needs administrator rights" : "");
        public string UndoLabel => $"Undo {Tweak.Name}";
    }

    public TweaksView()
    {
        InitializeComponent();
        TweakList.ItemsSource = TweakCatalog.All().Select(t => new Card { Tweak = t }).ToList();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Card card) return;

        var admin = card.Tweak.NeedsAdmin
            ? "\n\nThis one changes a system-wide setting and needs administrator rights. If it is "
              + "refused, close the app, right-click it and choose Run as administrator."
            : "";

        if (MessageBox.Show(Window.GetWindow(this),
                $"{card.Name}\n\n{card.Description}{admin}",
                "Apply tweak", MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
        {
            Show(new TweakResult(true, "Cancelled — nothing was changed."));
            return;
        }

        Show(card.Tweak.Apply());
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Card card || card.Tweak.Undo is null) return;

        if (MessageBox.Show(Window.GetWindow(this),
                $"Put {card.Name} back to the Windows default?",
                "Undo tweak", MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        Show(card.Tweak.Undo());
    }

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
