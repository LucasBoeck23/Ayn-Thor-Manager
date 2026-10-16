using System.Diagnostics;
using Avalonia.Interactivity;
using AynThorManager.Infrastructure.Network;
using AynThorManager.Services.DirectConnection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AynThorManager.Desktop;

/// <summary>
/// "Conexão Direta": turns the PC into a Wi-Fi hotspot so the Thor connects to a
/// dedicated two-device network (no router, lower latency, more stable), then
/// discovers the Thor's IP and hands it to the existing adb connect flow.
///
/// Fully isolated from the normal Wi-Fi connection code in MainWindow.Connection.cs.
/// </summary>
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

        var iface = DetectWifiInterface();
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

    /// <summary>
    /// Discovers the ADB port on the known hotspot IP and connects. Separated out so
    /// the "Tentar de novo" button can re-run it: the Thor often finishes joining /
    /// opens its ADB port a moment after the first scan, so a re-scan then succeeds.
    /// </summary>
    private async Task DiscoverAndConnectAsync()
    {
        if (_directBusy || _directIp is null) return;
        _directBusy = true;
        BtnDirectRetry.IsEnabled = false;

        try
        {
            var ip = _directIp;

            // With the official adb (mDNS enabled) the Thor announces its wireless
            // debugging port, so AutoConnect finds it directly. If mDNS yields
            // nothing, fall back to scanning the port range on the discovered IP.
            ShowDirect($"Thor em {ip}. Descobrindo porta do ADB (mDNS)...");
            var auto = await _device.AutoConnectAsync(default);
            if (auto.IsSuccess)
            {
                await ShowConnectResult(auto);
                BtnDirectRetry.IsVisible = false;
                return;
            }

            ShowDirect($"Thor em {ip}. mDNS não anunciou; varrendo portas...");
            var connected = await ScanAndConnectAsync(ip, default);
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

    /// <summary>
    /// Scans the ADB wireless port range on <paramref name="ip"/> and tries to
    /// connect to each open port, stopping at the first that adb accepts. Returns
    /// true once connected (and the UI is updated via the normal connect result).
    /// </summary>
    private async Task<bool> ScanAndConnectAsync(string ip, CancellationToken ct)
    {
        var scanner = new AdbPortScanner();

        // Scan the most likely sub-range first (37000–44000 covers most devices);
        // only widen to the full range if nothing is found there. Keeps the common
        // case fast while still handling ports outside the usual window.
        if (await TryRangeAsync(scanner, ip, 37000, 44000, ct)) return true;
        if (await TryRangeAsync(scanner, ip, AdbPortScanner.DefaultStartPort, 36999, ct)) return true;
        if (await TryRangeAsync(scanner, ip, 44001, AdbPortScanner.DefaultEndPort, ct)) return true;

        return false;
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

    /// <summary>Finds the first Wi-Fi interface name via nmcli (e.g. "wlp2s0").</summary>
    private static string? DetectWifiInterface()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nmcli",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-t", "-f", "DEVICE,TYPE", "device" })
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(':');
                if (parts.Length >= 2 && parts[1].Trim() == "wifi")
                    return parts[0].Trim();
            }
        }
        catch { /* nmcli missing or failed */ }

        return null;
    }
}
