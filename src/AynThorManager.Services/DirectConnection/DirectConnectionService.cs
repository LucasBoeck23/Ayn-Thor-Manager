using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.DirectConnection;

public sealed class DirectConnectionService(
    IHotspotController hotspot,
    ILogger<DirectConnectionService> logger)
{
    /// <summary>Starts the hotspot. Returns the error text on failure.</summary>
    public async Task<HotspotResult> StartHotspotAsync(string ssid, string password, CancellationToken ct)
    {
        logger.LogInformation("Starting direct-connection hotspot {Ssid}", ssid);
        return await hotspot.StartAsync(ssid, password, ct);
    }

    public async Task StopHotspotAsync(CancellationToken ct)
    {
        logger.LogInformation("Stopping direct-connection hotspot");
        await hotspot.StopAsync(ct);
    }

    public async Task<string?> WaitForDeviceAsync(TimeSpan timeout, CancellationToken ct)
    {
        // Snapshot who is already present so we only report the newcomer.
        var known = (await hotspot.ListClientsAsync(ct))
            .Select(c => c.MacAddress)
            .ToHashSet();

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var clients = await hotspot.ListClientsAsync(ct);
            var newcomer = clients.FirstOrDefault(c => c.IsReachable && !known.Contains(c.MacAddress));
            if (newcomer is not null)
            {
                logger.LogInformation("Thor joined hotspot at {Ip} ({Mac})", newcomer.IpAddress, newcomer.MacAddress);
                return newcomer.IpAddress;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        return null;
    }
}
