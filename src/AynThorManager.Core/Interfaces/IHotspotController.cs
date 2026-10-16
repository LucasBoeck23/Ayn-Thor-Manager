using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

public interface IHotspotController
{
    Task<HotspotResult> StartAsync(string ssid, string password, CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    Task<IReadOnlyList<NetworkClient>> ListClientsAsync(CancellationToken ct);
}
