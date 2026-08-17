using System.Windows;

namespace AynThorManager.Desktop;

public partial class InputDialog : Window
{
    public string Result => InputBox.Text.Trim();

    public InputDialog(string prompt)
    {
        InitializeComponent();
        Prompt.Text = prompt;
        InputBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
