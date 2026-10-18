namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Owns an external game-streaming host process (Sunshine). Implemented in the
/// Infrastructure layer; the Services layer orchestrates it for reverse streaming.
/// </summary>
public interface IGameStreamHost
{
    /// <summary>True while the host process is running.</summary>
    bool IsRunning { get; }

    /// <summary>Raised (on a thread-pool thread) when the host process exits.</summary>
    event Action? Exited;

    /// <summary>
    /// Starts the host process with the given argument string. Throws if the
    /// binary cannot be launched (e.g. Sunshine not installed).
    /// </summary>
    void Start(string arguments);

    /// <summary>Stops the host process if it is running. Safe to call when idle.</summary>
    void Stop();
}
