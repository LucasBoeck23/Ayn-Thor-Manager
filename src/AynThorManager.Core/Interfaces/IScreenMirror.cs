namespace AynThorManager.Core.Interfaces;

public interface IScreenMirror
{
    bool IsRunning { get; }

    event Action? Exited;

    void Start(string arguments);

    void Stop();
}
