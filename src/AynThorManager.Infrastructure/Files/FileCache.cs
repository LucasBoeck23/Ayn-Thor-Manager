using System.Collections.Concurrent;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Files;

public sealed class FileCache : IDisposable
{
    private readonly IAdbFileGateway _gateway;

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    private int _disposed;

    public string CacheDirectory { get; }

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

    public string LocalPathFor(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            throw new ArgumentException("A remote path is required.", nameof(remotePath));

        return Path.Combine(CacheDirectory, EncodeRemotePath(remotePath));
    }

    public async Task<Result<string>> EnsureCachedAsync(string remotePath, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(remotePath))
            return Result<string>.Failure(InvalidPath());

        var localPath = LocalPathFor(remotePath);

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

    public Result<string> CreateLocal(string remotePath)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(remotePath))
            return Result<string>.Failure(InvalidPath());

        var localPath = LocalPathFor(remotePath);
        try
        {
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

    public bool IsDirty(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return false;

        return _entries.TryGetValue(remotePath, out var entry) && entry.Dirty;
    }

    public bool IsCached(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return false;

        return _entries.TryGetValue(remotePath, out var entry) && entry.Pulled;
    }

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
        }
    }

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
        }
    }

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

    private readonly record struct CacheEntry(bool Pulled, bool Dirty);
}
