namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Joins the PC to the Thor's own Wi-Fi hotspot and reports the Thor's IP
/// (the gateway of that network). Implemented in the Infrastructure layer.
/// </summary>
public interface IWifiConnection
{
    /// <summary>
    /// Connects to the hotspot <paramref name="ssid"/> and returns the Thor's IP
    /// (the default gateway on that interface), or an error message on failure.
    /// </summary>
    Task<(bool Ok, string? ThorIp, string? Error)> ConnectAsync(string ssid, string password, CancellationToken ct);

    /// <summary>Disconnects from the hotspot and removes the profile.</summary>
    Task DisconnectAsync(CancellationToken ct);
}
