using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RigBooster.Services;

namespace RigBooster.Views;

public partial class LicenseWindow : Window
{
    private int _attempts;

    public LicenseWindow() => InitializeComponent();

    // Block anything the key format cannot contain, rather than letting it fail on submit.
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
                ShowError("Enter a username, and a key of 4 to 32 letters or numbers.");
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
                    ? "That username and key do not match. Ask whoever gave you the key to check it."
                    : "That username and key do not match.");
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
