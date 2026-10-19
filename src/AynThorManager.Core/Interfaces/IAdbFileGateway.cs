using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// High-level file operations over the connected Thor, so the FUSE translator never has
/// to speak raw ADB. Implemented in the Infrastructure layer on top of
/// <see cref="IAdbCommandExecutor"/> using the active device serial: listings come from
/// <c>ls -la</c> parsed into <see cref="ThorFileEntry"/>, content moves per-file via
/// <c>pull</c>/<c>push</c>, and <c>rm</c>/<c>mkdir</c>/<c>mv</c> back the remaining
/// operations. Every method returns a <see cref="Result"/> so ADB failures surface as
/// clear outcomes the FUSE callbacks can map to <c>-errno</c> — not loose exceptions
/// (Req 2).
/// </summary>
public interface IAdbFileGateway
{
    /// <summary>
    /// Lists the entries directly under the remote directory <paramref name="remotePath"/>
    /// (names and metadata only; no content is transferred). Fails when the path is not a
    /// readable directory on the Thor.
    /// </summary>
    Task<Result<IReadOnlyList<ThorFileEntry>>> ListAsync(string remotePath, CancellationToken ct = default);

    /// <summary>
    /// Reads the metadata of a single remote node at <paramref name="remotePath"/>.
    /// Fails when the path does not exist or cannot be stat'd.
    /// </summary>
    Task<Result<ThorFileEntry>> StatAsync(string remotePath, CancellationToken ct = default);

    /// <summary>
    /// Transfers the remote file at <paramref name="remotePath"/> down to the local path
    /// <paramref name="localPath"/> (the file cache). Fails when the pull cannot complete.
    /// </summary>
    Task<Result> PullAsync(string remotePath, string localPath, CancellationToken ct = default);

    /// <summary>
    /// Transfers the local file at <paramref name="localPath"/> up to the remote path
    /// <paramref name="remotePath"/>. Fails when the push cannot complete.
    /// </summary>
    Task<Result> PushAsync(string localPath, string remotePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes the remote file or directory at <paramref name="remotePath"/>. Fails when
    /// the target cannot be removed.
    /// </summary>
    Task<Result> DeleteAsync(string remotePath, CancellationToken ct = default);

    /// <summary>
    /// Creates the remote directory at <paramref name="remotePath"/>. Fails when the
    /// directory cannot be created.
    /// </summary>
    Task<Result> MakeDirAsync(string remotePath, CancellationToken ct = default);

    /// <summary>
    /// Renames/moves the remote node from <paramref name="fromRemotePath"/> to
    /// <paramref name="toRemotePath"/>. Fails when the move cannot complete.
    /// </summary>
    Task<Result> RenameAsync(string fromRemotePath, string toRemotePath, CancellationToken ct = default);
}
