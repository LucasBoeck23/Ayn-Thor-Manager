using Avalonia.Interactivity;
using AynThorManager.Infrastructure.Network;

namespace AynThorManager.Desktop;

/// <summary>
/// Direct connection: the Thor runs its own 5 GHz Wi-Fi hotspot and the PC joins it.
/// This is far more reliable than making the PC the access point — joining a Wi-Fi
/// network is a plain, root-free client operation, and the Thor's radio creates a
/// clean 5 GHz AP. The PC connects, finds the Thor (the network gateway) and attaches
/// adb over it.
/// </summary>
public partial class MainWindow
{
    private readonly WifiConnectionService _wifi = new();
    private bool _directActive;
    private string? _directIp;
    private bool _directBusy;

    private async void BtnDirectConnect_Click(object? sender, RoutedEventArgs e)
    {
        if (_directActive) return;

        var ssid = (TxtThorSsid.Text ?? "").Trim();
        var password = (TxtThorPassword.Text ?? "").Trim();
        if (ssid.Length == 0)
        {
            ShowDirect("Informe o nome (SSID) do hotspot do Thor.");
            return;
        }

        BtnDirectConnect.IsEnabled = false;
        ShowDirect($"Conectando à rede \"{ssid}\" do Thor...");

        try
        {
            var (ok, thorIp, error) = await _wifi.ConnectAsync(ssid, password, default);
            if (!ok)
            {
                ShowDirect(error ?? "Falha ao conectar à rede do Thor.");
                await CleanupDirectAsync();
                return;
            }

            _directActive = true;
            _directIp = thorIp;
            BtnDirectConnect.IsVisible = false;
            BtnDirectStop.IsVisible = true;
            ShowDirect($"Conectado à rede do Thor (Thor em {thorIp}).\nProcurando a porta do ADB...");

            await DiscoverAndConnectAsync();
        }
        catch (Exception ex)
        {
            ShowDirect($"Erro na conexão direta: {ex.Message}");
            await CleanupDirectAsync();
        }
        finally
        {
            BtnDirectConnect.IsEnabled = true;
        }
    }

    private async void BtnDirectRetry_Click(object? sender, RoutedEventArgs e) => await DiscoverAndConnectAsync();

    // One-time USB setup: arms "adb tcpip 5555" so the Thor is reachable wirelessly
    // on a fixed port, even as a hotspot. After this the cable can be removed.
    private async void BtnPrepareUsb_Click(object? sender, RoutedEventArgs e)
    {
        BtnPrepareUsb.IsEnabled = false;
        ShowDirect("Procurando o Thor via USB...");
        try
        {
            var result = await _device.PrepareUsbTcpipAsync(default);
            ShowDirect(result.IsSuccess
                ? "Thor preparado! Pode tirar o cabo, ligar o hotspot do Thor e clicar em \"Conectar ao Thor\"."
                : result.Error!.Message);
        }
        catch (Exception ex)
        {
            ShowDirect($"Erro ao preparar via USB: {ex.Message}");
        }
        finally
        {
            BtnPrepareUsb.IsEnabled = true;
        }
    }

    private async Task DiscoverAndConnectAsync()
    {
        if (_directBusy || _directIp is null) return;
        _directBusy = true;
        BtnDirectRetry.IsEnabled = false;

        try
        {
            var ip = _directIp;
            // The Thor was armed with `adb tcpip 5555` over USB, so connect straight
            // to the fixed port — no need to scan the random wireless-debugging range.
            ShowDirect($"Thor em {ip}. Conectando ao ADB (porta 5555)...");
            var result = await _device.ConnectAsync($"{ip}:5555", default);
            if (result.IsSuccess)
            {
                await ShowConnectResult(result);
                BtnDirectRetry.IsVisible = false;
            }
            else
            {
                ShowDirect($"Thor em {ip}, mas não consegui conectar ao ADB.\n{result.Error!.Message}\n" +
                           "Prepare o Thor via USB (botão abaixo) e tente de novo.");
                BtnDirectRetry.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            ShowDirect($"Erro ao conectar ao ADB: {ex.Message}");
            BtnDirectRetry.IsVisible = true;
        }
        finally
        {
            _directBusy = false;
            BtnDirectRetry.IsEnabled = true;
        }
    }

    private async void BtnDirectStop_Click(object? sender, RoutedEventArgs e)
    {
        await CleanupDirectAsync();
        ShowDirect("Conexão encerrada. O Wi-Fi do PC volta ao normal.");
    }

    private async Task CleanupDirectAsync()
    {
        try { await _wifi.DisconnectAsync(default); } catch { }

        _directActive = false;
        _directIp = null;
        BtnDirectConnect.IsVisible = true;
        BtnDirectConnect.IsEnabled = true;
        BtnDirectStop.IsVisible = false;
        BtnDirectRetry.IsVisible = false;
    }

    private void ShowDirect(string message)
    {
        DirectMessage.Text = message;
        DirectMessage.IsVisible = true;
    }

}
