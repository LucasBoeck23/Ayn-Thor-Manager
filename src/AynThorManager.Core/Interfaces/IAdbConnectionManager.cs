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
    /// Observable stream of device status changes for real-time notifications.
    /// </summary>
    IObservable<DeviceStatus> StatusChanges { get; }

    /// <summary>
    /// Connects to a device at the specified IP address via ADB over Wi-Fi (TCP 5555).
    /// </summary>
    /// <param name="ipAddress">IPv4 address of the target device.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the new device status or an error.</returns>
    Task<Result<DeviceStatus>> ConnectAsync(string ipAddress, CancellationToken ct);

    /// <summary>
    /// Disconnects the currently connected device.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the updated device status or an error.</returns>
    Task<Result<DeviceStatus>> DisconnectAsync(CancellationToken ct);

    /// <summary>
    /// Gets whether a device is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Marks the device as disconnected. Used by the heartbeat service
    /// when consecutive failures are detected.
    /// </summary>
    /// <param name="message">Optional reason message for the disconnection.</param>
    void MarkDisconnected(string? message = null);
}
