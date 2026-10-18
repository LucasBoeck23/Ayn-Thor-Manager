using AynThorManager.Core.Interfaces;

namespace AynThorManager.Services.Tests.Fakes;

/// <summary>
/// Fake scrcpy mirror (<see cref="IScreenMirror"/>) used to build a real forward
/// <c>StreamingService</c> for mutual-exclusion tests. The reverse orchestrator only
/// reads <see cref="IsRunning"/> and calls <see cref="Stop"/> on the forward service, so
/// this records those without launching anything.
/// </summary>
internal sealed class FakeScreenMirror : IScreenMirror
{
    public bool IsRunning { get; set; }

    public List<string> StartArgs { get; } = [];
    public int StopCount { get; private set; }

    public event Action? Exited;

    public void Start(string arguments)
    {
        StartArgs.Add(arguments);
        IsRunning = true;
    }

    public void Stop()
    {
        StopCount++;
        IsRunning = false;
    }

    public void RaiseExited()
    {
        IsRunning = false;
        Exited?.Invoke();
    }
}
