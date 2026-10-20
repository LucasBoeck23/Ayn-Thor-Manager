namespace AynThorManager.Core.Interfaces;

public interface IWifiConnection
{
    Task<(bool Ok, string? ThorIp, string? Error)> ConnectAsync(string ssid, string password, CancellationToken ct);

    Task DisconnectAsync(CancellationToken ct);
}
