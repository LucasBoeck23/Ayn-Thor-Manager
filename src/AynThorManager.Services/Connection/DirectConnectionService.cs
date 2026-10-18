using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Connection;

/// <summary>
/// Orchestrates the "direct connection" flow: the Thor runs its own 5 GHz Wi-Fi
/// hotspot, the PC joins it, finds the Thor (the network gateway) and attaches adb
/// over the fixed tcpip port.
///
/// This owns the whole connect → discover → adb → retry state machine that used to
/// live in the UI. The UI just calls these methods and reacts to the results and the
/// <see cref="Progress"/> messages.
/// </summary>
public sealed class DirectConnectionService(
    DeviceConnection device,
    IWifiConnection wifi,
    ILogger<DirectConnectionService> logger)
{
    private const int AdbPort = 5555;

    private bool _active;
    private string? _thorIp;
    private bool _busy;

    /// <summary>True once the PC has joined the Thor's hotspot (until it is stopped).</summary>
    public bool IsActive => _active;

    /// <summary>Raised with human-readable status messages for the UI to display.</summary>
    public event Action<string>? Progress;

    private void Report(string message) => Progress?.Invoke(message);

    /// <summary>
    /// Joins the Thor's hotspot and connects adb to it. On success the connected
    /// <see cref="ThorDevice"/> is returned; the caller can inspect <see cref="IsActive"/>
    /// to know whether the Wi-Fi link is up even when the adb step needs a retry.
    /// </summary>
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

    /// <summary>
    /// Attaches adb to the Thor on the fixed tcpip port. The Thor must have been armed
    /// once with <see cref="DeviceConnection.PrepareUsbTcpipAsync"/> over USB. Safe to
    /// call again as a retry; returns a failure (not an exception) when it can't connect.
    /// </summary>
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

    /// <summary>Arms the Thor over USB (one-time) so it is reachable wirelessly on the fixed port.</summary>
    public async Task<Result<ThorDevice>> PrepareUsbAsync(CancellationToken ct)
    {
        Report("Procurando o Thor via USB...");
        var result = await device.PrepareUsbTcpipAsync(ct);
        Report(result.IsSuccess
            ? "Thor preparado! Pode tirar o cabo, ligar o hotspot do Thor e clicar em \"Conectar ao Thor\"."
            : result.Error!.Message);
        return result;
    }

    /// <summary>Leaves the Thor's hotspot and resets the state. The PC's Wi-Fi returns to normal.</summary>
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
