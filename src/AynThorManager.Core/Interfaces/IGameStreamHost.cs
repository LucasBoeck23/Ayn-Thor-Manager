namespace AynThorManager.Core.Interfaces;

public interface IGameStreamHost
{
    bool IsRunning { get; }

    event Action? Exited;

    void Start(string arguments);

    void Stop();
}
