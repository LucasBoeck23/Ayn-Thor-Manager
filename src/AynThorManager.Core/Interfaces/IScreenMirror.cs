namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Owns an external screen-mirroring process (scrcpy). Implemented in the
/// Infrastructure layer; the Services layer builds the argument string and drives it.
/// </summary>
public interface IScreenMirror
{
    /// <summary>True while the mirroring process is running.</summary>
    bool IsRunning { get; }

    /// <summary>Raised (on a thread-pool thread) when the mirroring process exits.</summary>
    event Action? Exited;

    /// <summary>
    /// Starts the mirroring process with the given argument string. Throws if the
    /// binary cannot be launched (e.g. scrcpy not installed).
    /// </summary>
    void Start(string arguments);

    /// <summary>Stops the mirroring process if it is running. Safe to call when idle.</summary>
    void Stop();
}
