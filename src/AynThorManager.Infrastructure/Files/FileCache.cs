using System.Collections.Concurrent;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Files;

/// <summary>
/// The per-mount local file cache that backs the FUSE translator (Req 2.2/2.3). It owns a
/// private temporary directory under <c>/tmp</c> and maps each Thor remote path to a local
/// file inside it: content is pulled from the Thor on demand the first time a file is
/// opened (<see cref="EnsureCachedAsync"/>), reads and writes then run against the fast
/// local copy, writes flag the file "dirty" (<see cref="MarkDirty"/>), and a dirty file is
/// pushed back to the Thor when it is flushed/closed (<see cref="FlushAsync"/>). On unmount
/// the whole directory is deleted (<see cref="Dispose"/>), so nothing is left in
/// <c>/tmp</c> (Req 4.4).
///
/// The remote→local path mapping and the dirty bookkeeping are the isolable, I/O-free
/// parts (<see cref="LocalPathFor"/>, <see cref="IsDirty"/>) and are unit-tested directly;
/// the pull/push themselves are delegated to <see cref="IAdbFileGateway"/> so this class
/// never speaks raw ADB. Gateway failures surface as <see cref="Result"/> errors, never
/// loose exceptions, so the FUSE callbacks can map them to <c>-errno</c> instead of
/// crashing the mount.
/// </summary>
public sealed class FileCache : IDisposable
{
    private readonly IAdbFileGateway _gateway;

    // Thor remote path -> the state of its local cache copy. Concurrent because FUSE
    // callbacks for different paths can run on different kernel threads.
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    private int _disposed;

    /// <summary>The private temporary directory (under <c>/tmp</c>) that holds the cache.</summary>
    public string CacheDirectory { get; }

    /// <summary>
    /// Creates a cache for one mount, rooting a fresh unique directory under
    /// <paramref name="baseTempDirectory"/> (defaults to the system temp dir, i.e.
    /// <c>/tmp</c> on Linux). A per-mount subdirectory keeps each mount's files isolated
    /// and makes cleanup a single recursive delete.
    /// </summary>
    public FileCache(IAdbFileGateway gateway, string? baseTempDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        _gateway = gateway;

        var root = string.IsNullOrWhiteSpace(baseTempDirectory)
            ? Path.GetTempPath()
            : baseTempDirectory;

        CacheDirectory = Path.Combine(root, $"ayn-thor-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(CacheDirectory);
    }

    /// <summary>
    /// The local cache file path a given Thor <paramref name="remotePath"/> maps to. Pure
    /// and deterministic: the same remote path always maps to the same local path for this
    /// cache. The remote path's structure is flattened into a single file name (slashes
    /// escaped) so arbitrary depth never collides and never escapes the cache directory.
    /// </summary>
    public string LocalPathFor(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            throw new ArgumentException("A remote path is required.", nameof(remotePath));

        return Path.Combine(CacheDirectory, EncodeRemotePath(remotePath));
    }

    /// <summary>
    /// Guarantees the file at <paramref name="remotePath"/> exists in the cache, pulling it
    /// from the Thor the first time (Req 2.2). Subsequent calls for the same path are a
    /// no-op so reads keep serving from the local copy rather than re-downloading. Returns
    /// the local path on success.
    /// </summary>
    public async Task<Result<string>> EnsureCachedAsync(string remotePath, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(remotePath))
            return Result<string>.Failure(InvalidPath());

        var localPath = LocalPathFor(remotePath);

        // Already pulled (and not since removed): serve from the existing local copy.
        if (_entries.TryGetValue(remotePath, out var existing)
            && existing.Pulled
            && File.Exists(localPath))
        {
            return Result<string>.Success(localPath);
        }

        var pull = await _gateway.PullAsync(remotePath, localPath, ct);
        if (!pull.IsSuccess)
            return Result<string>.Failure(pull.Error!);

        _entries[remotePath] = new CacheEntry(Pulled: true, Dirty: false);
        return Result<string>.Success(localPath);
    }

    /// <summary>
    /// Registers a freshly created local file for <paramref name="remotePath"/> without
    /// pulling anything (for <c>create</c>/<c>truncate</c>, where there is no remote
    /// content yet). Creates the empty local file and marks it dirty so the eventual flush
    /// pushes it to the Thor (Req 2.3). Returns the local path.
    /// </summary>
    public Result<string> CreateLocal(string remotePath)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(remotePath))
            return Result<string>.Failure(InvalidPath());

        var localPath = LocalPathFor(remotePath);
        try
        {
            // Truncate/create an empty local file as the backing store.
            using (File.Create(localPath)) { }
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(new Error(
                "CACHE_WRITE_FAILED",
                $"Não foi possível criar o arquivo de cache para {remotePath}: {ex.Message}"));
        }

        _entries[remotePath] = new CacheEntry(Pulled: true, Dirty: true);
        return Result<string>.Success(localPath);
    }

    /// <summary>
    /// Flags the cached file for <paramref name="remotePath"/> as dirty, i.e. modified
    /// locally and not yet pushed. A path that was never cached is still tracked as dirty
    /// so a write that races ahead of the pull is not lost. Idempotent.
    /// </summary>
    public void MarkDirty(string remotePath)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(remotePath))
            return;

        _entries.AddOrUpdate(
            remotePath,
            _ => new CacheEntry(Pulled: false, Dirty: true),
            (_, current) => current with { Dirty = true });
    }

    /// <summary>
    /// True when the cached file for <paramref name="remotePath"/> has unpushed local
    /// changes. Used by the FUSE <c>flush</c>/<c>release</c> path to decide whether a push
    /// is needed.
    /// </summary>
    public bool IsDirty(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return false;

        return _entries.TryGetValue(remotePath, out var entry) && entry.Dirty;
    }

    /// <summary>True when the file for <paramref name="remotePath"/> has been pulled into the cache.</summary>
    public bool IsCached(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return false;

        return _entries.TryGetValue(remotePath, out var entry) && entry.Pulled;
    }

    /// <summary>
    /// Pushes the cached file for <paramref name="remotePath"/> back to the Thor when it is
    /// dirty, then clears the dirty flag (Req 2.3). When the file is not dirty this is a
    /// no-op success — no needless push happens (correctness Property 4). The dirty flag is
    /// only cleared after a successful push, so a failed push stays pending and can be
    /// retried on the next flush.
    /// </summary>
    public async Task<Result> FlushAsync(string remotePath, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(remotePath))
            return Result.Failure(InvalidPath());

        if (!IsDirty(remotePath))
            return Result.Success();

        var localPath = LocalPathFor(remotePath);
        var push = await _gateway.PushAsync(localPath, remotePath, ct);
        if (!push.IsSuccess)
            return Result.Failure(push.Error!);

        if (_entries.TryGetValue(remotePath, out var entry))
            _entries[remotePath] = entry with { Dirty = false };

        return Result.Success();
    }

    /// <summary>
    /// Forgets the cache entry for <paramref name="remotePath"/> and deletes its local
    /// copy (for <c>unlink</c>/<c>rename</c>, where the old local file is no longer valid).
    /// Best-effort on the local delete; never throws.
    /// </summary>
    public void Evict(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return;

        _entries.TryRemove(remotePath, out _);

        try
        {
            var localPath = Path.Combine(CacheDirectory, EncodeRemotePath(remotePath));
            if (File.Exists(localPath))
                File.Delete(localPath);
        }
        catch
        {
            // Best-effort: a stale local file is cleaned up with the whole directory on unmount.
        }
    }

    /// <summary>
    /// Deletes the entire per-mount cache directory on unmount so nothing is left in
    /// <c>/tmp</c> (Req 4.4). Best-effort and idempotent; safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _entries.Clear();

        try
        {
            if (Directory.Exists(CacheDirectory))
                Directory.Delete(CacheDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup: the OS clears /tmp eventually and we never want unmount
            // to fail just because a file was briefly locked.
        }
    }

    /// <summary>
    /// Flattens a remote path into a single, collision-free file name by percent-escaping
    /// the path separators and the escape character itself. Because the result contains no
    /// directory separators, the mapped local file can never escape the cache directory
    /// (no path traversal), and distinct remote paths always map to distinct names.
    /// </summary>
    private static string EncodeRemotePath(string remotePath)
    {
        return remotePath
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("/", "%2F", StringComparison.Ordinal);
    }

    private static Error InvalidPath() =>
        new("INVALID_PATH", "O caminho informado é inválido.");

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(FileCache));
    }

    /// <summary>
    /// The bookkeeping for one cached remote path: whether its content has been pulled into
    /// the cache, and whether it has unpushed local changes.
    /// </summary>
    private readonly record struct CacheEntry(bool Pulled, bool Dirty);
}
