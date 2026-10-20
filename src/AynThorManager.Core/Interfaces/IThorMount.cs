namespace AynThorManager.Core.Interfaces;

public interface IThorMount
{
    bool IsMounted { get; }

    string? MountPoint { get; }

    Task MountAsync(string serial, CancellationToken ct = default);

    Task UnmountAsync(CancellationToken ct = default);
}
