using AynThorManager.Core.Interfaces;

namespace AynThorManager.Services.Tests.Fakes;

internal sealed class FakeGameStreamHost : IGameStreamHost
{
    public Exception? StartThrows { get; set; }

    public bool IsRunning { get; set; }

    public List<string> StartArgs { get; } = [];
    public int StartCount => StartArgs.Count;
    public int StopCount { get; private set; }

    public event Action? Exited;

    public void Start(string arguments)
    {
        StartArgs.Add(arguments);
        if (StartThrows is not null)
            throw StartThrows;
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
