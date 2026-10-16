using Avalonia.Media;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;

namespace AynThorManager.Desktop;

/// <summary>
/// Shared connection UI helpers. The only way to connect is the direct hotspot
/// flow (see MainWindow.DirectConnect.cs); this file keeps the result rendering
/// and the status header in sync, plus the shared brushes used across the UI.
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush SuccessBrush = SolidColorBrush.Parse("#00e676");
    private static readonly IBrush DangerBrush = SolidColorBrush.Parse("#ff5252");
    private static readonly IBrush AccentBrush = SolidColorBrush.Parse("#00d4ff");
    private static readonly IBrush SecondaryBrush = SolidColorBrush.Parse("#78909c");

    /// <summary>Renders the outcome of a connection and loads the file browser on success.</summary>
    private async Task ShowConnectResult(Result<ThorDevice> result)
    {
        if (!result.IsSuccess)
        {
            ConnMessage.Text = result.Error!.Message;
            return;
        }

        ConnMessage.Text = "Conectado!";

        _currentPath = "/storage/";
        await LoadDirectory();
    }

    /// <summary>Keeps the header in sync with the real connection, including drops.</summary>
    private void OnDeviceChanged(ThorDevice? device)
    {
        if (device is null)
        {
            StatusDot.Fill = DangerBrush;
            StatusText.Text = "Desconectado";
            StopNetStats();
            return;
        }

        StatusDot.Fill = SuccessBrush;
        StatusText.Text = $"Conectado ({device.Model})";
        StartNetStats();
    }
}
