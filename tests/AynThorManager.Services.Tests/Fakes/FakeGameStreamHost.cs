using AynThorManager.Core.Interfaces;

namespace AynThorManager.Services.Tests.Fakes;

/// <summary>
/// Scripted stand-in for the Sunshine host (<see cref="IGameStreamHost"/>). Lets a test
/// toggle <see cref="IsRunning"/>, see how many times (and with what args) Start/Stop
/// were called, make Start throw (to exercise SUNSHINE_START_FAILED), and raise
/// <see cref="Exited"/> on demand.
/// </summary>
internal sealed class FakeGameStreamHost : IGameStreamHost
{
    /// <summary>When set, <see cref="Start"/> throws this instead of running.</summary>
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

    /// <summary>Simulates the host process exiting on its own.</summary>
    public void RaiseExited()
    {
        IsRunning = false;
        Exited?.Invoke();
    }
}
