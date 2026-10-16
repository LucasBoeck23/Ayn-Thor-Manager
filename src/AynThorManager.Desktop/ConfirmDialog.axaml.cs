using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AynThorManager.Desktop;

/// <summary>Yes/No confirmation dialog, returning true when the user confirms.</summary>
public partial class ConfirmDialog : Window
{
    // Parameterless ctor required by the Avalonia XAML tooling.
    public ConfirmDialog() : this("Confirmar", "") { }

    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Close(true);
            else if (e.Key == Key.Escape) Close(false);
        };
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
