namespace AynThorManager.Desktop.Embedding;

/// <summary>
/// Reparents an external process window (scrcpy) so it becomes a child of a
/// native host owned by this application, reproducing the embedded-mirror
/// behavior of the original Windows build on every supported platform.
/// </summary>
public interface IScreenEmbedder
{
    /// <summary>
    /// Waits for the process to create its top-level window and returns its
    /// native handle, or <see cref="nint.Zero"/> if none appeared in time.
    /// </summary>
    Task<nint> WaitForWindowAsync(int processId, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Reparents <paramref name="childWindow"/> into <paramref name="hostHandle"/>
    /// and makes it fill the host, removing its own decorations.
    /// </summary>
    void Embed(nint hostHandle, nint childWindow);

    /// <summary>
    /// Resizes the embedded child to the given pixel size inside its host.
    /// </summary>
    void Resize(nint childWindow, int width, int height);
}
