using AynThorManager.Infrastructure.Streaming;

namespace AynThorManager.Infrastructure.Tests.Streaming;

/// <summary>
/// Scripted stand-in for a real OS process. Drives the started / running / exited
/// lifecycle that <see cref="SunshineHost"/> tracks, without spawning anything.
/// </summary>
internal sealed class FakeHostProcess : IHostProcess
{
    public bool HasExited { get; private set; }
    public bool EnableRaisingEvents { get; set; }
    public int KillCount { get; private set; }
    public bool? KilledEntireProcessTree { get; private set; }
    public bool Disposed { get; private set; }

    public event EventHandler? Exited;

    /// <summary>Simulates the process ending on its own (crash or normal exit).</summary>
    public void SimulateExit()
    {
        HasExited = true;
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public void Kill(bool entireProcessTree)
    {
        KillCount++;
        KilledEntireProcessTree = entireProcessTree;
        HasExited = true;
    }

    public void Dispose() => Disposed = true;

    /// <summary>Number of handlers currently attached to <see cref="Exited"/>.</summary>
    public int ExitedSubscriberCount => Exited?.GetInvocationList().Length ?? 0;
}
