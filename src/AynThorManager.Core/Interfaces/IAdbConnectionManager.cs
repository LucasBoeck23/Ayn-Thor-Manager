using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

public interface IAdbConnectionManager
{
    DeviceStatus CurrentStatus { get; }

    Task<Result<DeviceStatus>> ConnectAsync(string ipAddress, CancellationToken ct);

    Task<Result<DeviceStatus>> DisconnectAsync(CancellationToken ct);

    bool IsConnected { get; }
}
