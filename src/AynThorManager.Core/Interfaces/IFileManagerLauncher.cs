namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Opens the native file manager at a local path (folder). Implemented in the
/// Infrastructure layer (e.g. <c>xdg-open</c> / the Avalonia launcher); the Services
/// layer uses it to reveal the mount point after mounting the Thor. Best-effort: the
/// implementation only opens the system file manager and never navigates files itself.
/// </summary>
public interface IFileManagerLauncher
{
    /// <summary>
    /// Opens the native file manager at <paramref name="path"/> (a local folder).
    /// Throws if the file manager cannot be launched.
    /// </summary>
    Task OpenAsync(string path, CancellationToken ct = default);
}
