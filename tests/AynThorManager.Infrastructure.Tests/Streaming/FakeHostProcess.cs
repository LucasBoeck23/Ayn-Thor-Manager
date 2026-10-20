using AynThorManager.Infrastructure.Streaming;

namespace AynThorManager.Infrastructure.Tests.Streaming;

internal sealed class FakeHostProcess : IHostProcess
{
    public bool HasExited { get; private set; }
    public bool EnableRaisingEvents { get; set; }
    public int KillCount { get; private set; }
    public bool? KilledEntireProcessTree { get; private set; }
    public bool Disposed { get; private set; }

    public event EventHandler? Exited;

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

    public int ExitedSubscriberCount => Exited?.GetInvocationList().Length ?? 0;
}
