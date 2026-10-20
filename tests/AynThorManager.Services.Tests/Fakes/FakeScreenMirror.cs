using AynThorManager.Core.Interfaces;

namespace AynThorManager.Services.Tests.Fakes;

internal sealed class FakeScreenMirror : IScreenMirror
{
    public bool IsRunning { get; set; }

    public List<string> StartArgs { get; } = [];
    public int StopCount { get; private set; }

    public event Action? Exited;

    public void Start(IReadOnlyList<string> instances)
    {
        StartArgs.AddRange(instances);
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
