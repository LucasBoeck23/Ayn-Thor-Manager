using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Manages the ADB connection lifecycle and device status.
/// </summary>
public interface IAdbConnectionManager
{
    /// <summary>
    /// Gets the current device connection status.
    /// </summary>
    DeviceStatus CurrentStatus { get; }

    /// <summary>
    /// Connects to a device at the specified IP address via ADB over Wi-Fi.
    /// </summary>
    Task<Result<DeviceStatus>> ConnectAsync(string ipAddress, CancellationToken ct);

    /// <summary>
    /// Disconnects the currently connected device.
    /// </summary>
    Task<Result<DeviceStatus>> DisconnectAsync(CancellationToken ct);

    /// <summary>
    /// Gets whether a device is currently connected.
    /// </summary>
    bool IsConnected { get; }
}
