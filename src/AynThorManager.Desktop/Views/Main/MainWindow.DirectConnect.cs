using Avalonia.Interactivity;
using AynThorManager.Infrastructure.Network;
using AynThorManager.Services.DirectConnection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private const string DirectSsid = "AynThorLink";
    private const string DirectPassword = "aynthor-link";

    private DirectConnectionService? _directService;
    private bool _directActive;
    private string? _directIp;      // last IP discovered on the hotspot (for retry)
    private bool _directBusy;       // a discovery/scan pass is running

    private async void BtnDirectConnect_Click(object? sender, RoutedEventArgs e)
    {
        if (_directActive) return;

        var iface = await HotspotController.DetectWifiInterfaceAsync();
        if (iface is null)
        {
            ShowDirect("Nenhuma interface Wi-Fi encontrada para criar o hotspot.");
            return;
        }

        BtnDirectConnect.IsEnabled = false;
        ShowDirect("Criando hotspot...");

        var controller = new HotspotController(iface, "AynThorDirect");
        _directService = new DirectConnectionService(controller, NullLogger<DirectConnectionService>.Instance);

        try
        {
            var started = await _directService.StartHotspotAsync(DirectSsid, DirectPassword, default);
            if (!started.IsSuccess)
            {
                ShowDirect(started.Error ?? "Falha ao criar o hotspot.");
                await CleanupDirectAsync();
                return;
            }

            _directActive = true;
            BtnDirectConnect.IsVisible = false;
            BtnDirectStop.IsVisible = true;
            ShowDirect($"Hotspot ativo.\nNo Thor, conecte no Wi-Fi \"{DirectSsid}\" (senha: {DirectPassword}) e ative a Depuração sem fio.\nAguardando o Thor entrar...");

            // Wait for the Thor to join and get an IP on the dedicated network.
            var ip = await _directService.WaitForDeviceAsync(TimeSpan.FromSeconds(90), default);
            if (ip is null)
            {
                ShowDirect("O Thor não entrou no hotspot a tempo. Verifique se conectou no Wi-Fi correto.");
                return;
            }

            _directIp = ip;
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

    private async Task DiscoverAndConnectAsync()
    {
        if (_directBusy || _directIp is null) return;
        _directBusy = true;
        BtnDirectRetry.IsEnabled = false;

        try
        {
            var ip = _directIp;
            ShowDirect($"Thor em {ip}. Procurando a porta do ADB...");
            var scanner = new AdbPortScanner();
            var connected = await TryRangeAsync(scanner, ip, AdbPortScanner.DefaultStartPort, AdbPortScanner.DefaultEndPort, default);
            if (connected)
            {
                BtnDirectRetry.IsVisible = false;
            }
            else
            {
                ShowDirect($"Thor em {ip}, mas não achei a porta do ADB ainda.\n" +
                           "Confirme que a 'Depuração sem fio' está ativa no Thor e toque em Tentar de novo.");
                BtnDirectRetry.IsVisible = true; // let the user re-scan once the port is up
            }
        }
        catch (Exception ex)
        {
            ShowDirect($"Erro ao procurar o ADB: {ex.Message}");
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
        ShowDirect("Conexão direta encerrada. O Wi-Fi do PC volta ao normal.");
    }

    private async Task CleanupDirectAsync()
    {
        if (_directService is not null)
        {
            try { await _directService.StopHotspotAsync(default); } catch { /* best effort */ }
            _directService = null;
        }

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

    private async Task<bool> TryRangeAsync(AdbPortScanner scanner, string ip, int start, int end, CancellationToken ct)
    {
        ShowDirect($"Procurando a porta do ADB em {ip} ({start}-{end})...");
        var openPorts = await scanner.ScanAsync(ip, start, end, ct);
        if (openPorts.Count == 0) return false;

        ShowDirect($"{openPorts.Count} porta(s) aberta(s). Testando ADB...");
        foreach (var port in openPorts)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _device.ConnectAsync($"{ip}:{port}", ct);
            if (result.IsSuccess)
            {
                await ShowConnectResult(result);
                return true;
            }
        }

        return false;
    }

}
