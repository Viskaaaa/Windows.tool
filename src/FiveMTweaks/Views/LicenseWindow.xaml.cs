using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FiveMTweaks.Services;

namespace FiveMTweaks.Views;

public partial class LicenseWindow : Window
{
    private int _attempts;

    public LicenseWindow()
    {
        InitializeComponent();

        // Honour the motion preference here too, not just in the main window.
        if (!ThemeService.EffectsAllowed) CursorGlow.Visibility = Visibility.Collapsed;
    }

    // Block anything the key format cannot contain, rather than letting it fail on submit.
    /// <summary>Moves the glow with the pointer, exactly as the main window does.</summary>
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!ThemeService.EffectsAllowed || ActualWidth <= 0 || ActualHeight <= 0) return;

        var p = e.GetPosition(this);
        var point = new Point(p.X / ActualWidth, p.Y / ActualHeight);
        GlowBrush.Center = point;
        GlowBrush.GradientOrigin = point;
    }

    private void KeyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsAsciiLetterOrDigit);

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        var result = LicenseService.Validate(UserBox.Text, KeyBox.Text, RememberBox.IsChecked == true);

        switch (result)
        {
            case LicenseService.Result.Ok:
                DialogResult = true;
                return;

            case LicenseService.Result.BadFormat:
                ShowError("Enter a key of 4 to 32 letters or numbers.");
                break;

            case LicenseService.Result.TableMissing:
                ShowError("No key list was built into this app. Whoever built it needs to run the "
                          + "licensegen pack step and rebuild.");
                break;

            case LicenseService.Result.TableUnreadable:
                ShowError("The key list will not decrypt — the build secret does not match the one "
                          + "it was packed with. Repack licenses.dat with the secret in LicenseService.cs.");
                break;

            default:
                _attempts++;
                ShowError(_attempts >= 3
                    ? "That key was not recognised. Ask whoever gave it to you to check it."
                    : "That key was not recognised.");
                break;
        }

        KeyBox.SelectAll();
        KeyBox.Focus();
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBox.Visibility = Visibility.Visible;
    }
}
