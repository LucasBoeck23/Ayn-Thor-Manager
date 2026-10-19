using Avalonia.Interactivity;

namespace AynThorManager.Desktop;

/// <summary>
/// Direct connection UI: the Thor runs its own 5 GHz Wi-Fi hotspot and the PC joins it.
/// All the connect/discover/retry orchestration lives in <c>DirectConnectionService</c>;
/// this partial only wires buttons to it, shows its progress messages, and toggles
/// which buttons are visible.
/// </summary>
public partial class MainWindow
{
    private async void BtnDirectConnect_Click(object? sender, RoutedEventArgs e)
    {
        if (_directConnection.IsActive) return;

        BtnDirectConnect.IsEnabled = false;
        try
        {
            var ssid = TxtThorSsid.Text ?? "";
            var password = TxtThorPassword.Text ?? "";
            var result = await _directConnection.ConnectAsync(ssid, password, default);

            if (!_directConnection.IsActive)
                return; // Wi-Fi failed; the service already reported the error.

            // Joined the hotspot: remember the SSID (config) and password (keyring) so
            // the fields pre-fill next time. Best-effort, must not block the flow.
            await SaveConnectionAsync(ssid.Trim(), password.Trim());

            BtnDirectConnect.IsVisible = false;
            BtnDirectStop.IsVisible = true;
            HandleDiscoverResult(result);
        }
        finally
        {
            BtnDirectConnect.IsEnabled = true;
        }
    }

    private async void BtnDirectRetry_Click(object? sender, RoutedEventArgs e)
    {
        BtnDirectRetry.IsEnabled = false;
        try
        {
            var result = await _directConnection.DiscoverAndConnectAsync(default);
            HandleDiscoverResult(result);
        }
        finally
        {
            BtnDirectRetry.IsEnabled = true;
        }
    }

    private async void BtnPrepareUsb_Click(object? sender, RoutedEventArgs e)
    {
        BtnPrepareUsb.IsEnabled = false;
        try { await _directConnection.PrepareUsbAsync(default); }
        finally { BtnPrepareUsb.IsEnabled = true; }
    }

    private async void BtnDirectStop_Click(object? sender, RoutedEventArgs e)
    {
        await _directConnection.StopAsync(default);
        BtnDirectConnect.IsVisible = true;
        BtnDirectConnect.IsEnabled = true;
        BtnDirectStop.IsVisible = false;
        BtnDirectRetry.IsVisible = false;
    }

    private void HandleDiscoverResult(Core.Models.Result<AynThorManager.Services.Connection.ThorDevice> result)
    {
        if (result.IsSuccess)
        {
            ShowConnectResult(result);
            BtnDirectRetry.IsVisible = false;
        }
        else
        {
            BtnDirectRetry.IsVisible = true;
        }
    }

    private void ShowDirect(string message)
    {
        DirectMessage.Text = message;
        DirectMessage.IsVisible = true;
    }
}
