using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Sends real-time notifications to connected WebSocket clients.
/// </summary>
public interface IWebSocketNotifier
{
    /// <summary>
    /// Sends transfer progress update to all connected clients.
    /// </summary>
    /// <param name="progress">Current transfer progress data.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendTransferProgressAsync(TransferProgress progress, CancellationToken ct);

    /// <summary>
    /// Sends device status change notification to all connected clients.
    /// </summary>
    /// <param name="status">Updated device status.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendDeviceStatusAsync(DeviceStatus status, CancellationToken ct);

    /// <summary>
    /// Sends transfer completed notification to all connected clients.
    /// </summary>
    /// <param name="result">Transfer result with file outcomes.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendTransferCompletedAsync(TransferResult result, CancellationToken ct);

    /// <summary>
    /// Sends transfer failed notification to all connected clients.
    /// </summary>
    /// <param name="failure">Transfer result containing failure information.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendTransferFailedAsync(TransferResult failure, CancellationToken ct);
}
