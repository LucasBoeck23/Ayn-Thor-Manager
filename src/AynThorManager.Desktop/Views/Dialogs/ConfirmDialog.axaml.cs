using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AynThorManager.Desktop;

public partial class ConfirmDialog : Window
{
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
