using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AynThorManager.Desktop;

public partial class InputDialog : Window
{
    public string Result => InputBox.Text?.Trim() ?? string.Empty;

    public InputDialog() : this("") { }

    public InputDialog(string prompt, string initialValue = "")
    {
        InitializeComponent();
        Prompt.Text = prompt;
        InputBox.Text = initialValue;

        InputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Close(true);
            else if (e.Key == Key.Escape) Close(false);
        };

        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            InputBox.Focus();
            InputBox.SelectAll(); 
        });
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
