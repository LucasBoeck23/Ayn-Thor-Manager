namespace AynThorManager.Core.Interfaces;

public interface IScreenMirror
{
    bool IsRunning { get; }

    event Action? Exited;

    void Start(IReadOnlyList<string> instances);

    void Stop();
}
