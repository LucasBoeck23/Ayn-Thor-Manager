using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Controls a PC-hosted Wi-Fi hotspot used to create a direct, dedicated link
/// between the PC and the Thor (no router in the middle). Implemented in the
/// Infrastructure layer (NetworkManager/nmcli on Linux).
/// </summary>
public interface IHotspotController
{
    /// <summary>Brings up the hotspot with the given SSID/password.</summary>
    Task<HotspotResult> StartAsync(string ssid, string password, CancellationToken ct);

    /// <summary>Tears the hotspot down and frees the adapter.</summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>Devices currently seen on the hotspot interface.</summary>
    Task<IReadOnlyList<NetworkClient>> ListClientsAsync(CancellationToken ct);
}
