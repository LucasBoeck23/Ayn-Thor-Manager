using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Connection;

public sealed class DirectConnectionService(
    DeviceConnection device,
    IWifiConnection wifi,
    ILogger<DirectConnectionService> logger)
{
    private const int AdbPort = 5555;

    private bool _active;
    private string? _thorIp;
    private bool _busy;

    public bool IsActive => _active;

    public event Action<string>? Progress;

    private void Report(string message) => Progress?.Invoke(message);

    public async Task<Result<ThorDevice>> ConnectAsync(string ssid, string password, CancellationToken ct)
    {
        if (_active)
            return Result<ThorDevice>.Failure(new Error("ALREADY_ACTIVE", "A conexão direta já está ativa."));

        ssid = (ssid ?? "").Trim();
        password = (password ?? "").Trim();
        if (ssid.Length == 0)
            return Result<ThorDevice>.Failure(new Error("NO_SSID", "Informe o nome (SSID) do hotspot do Thor."));

        Report($"Conectando à rede \"{ssid}\" do Thor...");

        var (ok, thorIp, error) = await wifi.ConnectAsync(ssid, password, ct);
        if (!ok)
        {
            await CleanupAsync(ct);
            return Result<ThorDevice>.Failure(new Error("WIFI_FAILED", error ?? "Falha ao conectar à rede do Thor."));
        }

        _active = true;
        _thorIp = thorIp;
        logger.LogInformation("Joined Thor hotspot '{Ssid}', Thor at {ThorIp}", ssid, thorIp);
        Report($"Conectado à rede do Thor (Thor em {thorIp}).\nProcurando a porta do ADB...");

        return await DiscoverAndConnectAsync(ct);
    }

    public async Task<Result<ThorDevice>> DiscoverAndConnectAsync(CancellationToken ct)
    {
        if (_thorIp is null)
            return Result<ThorDevice>.Failure(new Error("NOT_ACTIVE", "Conecte-se à rede do Thor primeiro."));

        if (_busy)
            return Result<ThorDevice>.Failure(new Error("BUSY", "Conexão em andamento, aguarde."));

        _busy = true;
        try
        {
            var ip = _thorIp;
            Report($"Thor em {ip}. Conectando ao ADB (porta {AdbPort})...");

            var result = await device.ConnectAsync($"{ip}:{AdbPort}", ct);
            if (!result.IsSuccess)
            {
                Report($"Thor em {ip}, mas não consegui conectar ao ADB.\n{result.Error!.Message}\n" +
                       "Prepare o Thor via USB (botão abaixo) e tente de novo.");
            }

            return result;
        }
        finally
        {
            _busy = false;
        }
    }

    public async Task<Result<ThorDevice>> PrepareUsbAsync(CancellationToken ct)
    {
        Report("Procurando o Thor via USB...");
        var result = await device.PrepareUsbTcpipAsync(ct);
        Report(result.IsSuccess
            ? "Thor preparado! Pode tirar o cabo, ligar o hotspot do Thor e clicar em \"Conectar ao Thor\"."
            : result.Error!.Message);
        return result;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await CleanupAsync(ct);
        Report("Conexão encerrada. O Wi-Fi do PC volta ao normal.");
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        try { await wifi.DisconnectAsync(ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to disconnect Wi-Fi profile during cleanup"); }

        _active = false;
        _thorIp = null;
    }
}
