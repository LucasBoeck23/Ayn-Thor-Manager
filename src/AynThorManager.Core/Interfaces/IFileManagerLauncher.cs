namespace AynThorManager.Core.Interfaces;

public interface IFileManagerLauncher
{
    Task OpenAsync(string path, CancellationToken ct = default);
}
