namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Owns a native FUSE mount of the Thor's filesystem (a FUSE→ADB translator written in
/// this repo, backed by <c>libfuse</c>/<c>fusermount</c> — no third-party binary).
/// Implemented in the Infrastructure layer; the Services layer orchestrates it to expose
/// the Thor in the PC's native file manager. Implementations only mount/unmount over the
/// already connected ADB session — they never install software and never ask for sudo
/// (Req 1).
/// </summary>
public interface IThorMount
{
    /// <summary>True while the Thor is mounted at <see cref="MountPoint"/>.</summary>
    bool IsMounted { get; }

    /// <summary>The local mount point path, or <c>null</c> when nothing is mounted.</summary>
    string? MountPoint { get; }

    /// <summary>
    /// Mounts the Thor identified by <paramref name="serial"/> (the already connected
    /// ADB serial/address) at a per-user mount point. Throws if the mount cannot be
    /// started (e.g. <c>libfuse</c>/<c>fusermount</c> not available).
    /// </summary>
    Task MountAsync(string serial, CancellationToken ct = default);

    /// <summary>
    /// Unmounts the Thor if it is mounted. Safe to call when nothing is mounted
    /// (idempotent); falls back to a lazy unmount when the normal unmount fails.
    /// </summary>
    Task UnmountAsync(CancellationToken ct = default);
}
