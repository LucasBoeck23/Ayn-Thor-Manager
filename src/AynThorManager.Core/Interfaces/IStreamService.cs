using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Manages Scrcpy streaming sessions (Thor screen → PC).
/// </summary>
public interface IStreamService
{
    /// <summary>Starts a Scrcpy streaming session to the connected device.</summary>
    Task<Result> StartAsync(CancellationToken ct);

    /// <summary>Stops the active Scrcpy session.</summary>
    Task<Result> StopAsync(CancellationToken ct);

    /// <summary>Whether a streaming session is currently active.</summary>
    bool IsStreaming { get; }
}
