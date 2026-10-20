using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Files;

namespace AynThorManager.Infrastructure.Tests.Files;

public sealed class FileCacheTests : IDisposable
{
    private readonly string _baseDir;

    public FileCacheTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"ayn-thor-cache-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_baseDir))
                Directory.Delete(_baseDir, recursive: true);
        }
        catch {  }
    }

    private sealed class FakeGateway : IAdbFileGateway
    {
        private readonly Result _pullResult;
        private readonly Result _pushResult;
        private readonly bool _materializeOnPull;

        public int PullCount { get; private set; }
        public int PushCount { get; private set; }
        public string? LastPushLocalPath { get; private set; }
        public string? LastPushRemotePath { get; private set; }
        public string? LastPullRemotePath { get; private set; }

        public FakeGateway(Result? pull = null, Result? push = null, bool materializeOnPull = true)
        {
            _pullResult = pull ?? Result.Success();
            _pushResult = push ?? Result.Success();
            _materializeOnPull = materializeOnPull;
        }

        public Task<Result> PullAsync(string remotePath, string localPath, CancellationToken ct = default)
        {
            PullCount++;
            LastPullRemotePath = remotePath;
            if (_materializeOnPull && _pullResult.IsSuccess)
                File.WriteAllText(localPath, "pulled");
            return Task.FromResult(_pullResult);
        }

        public Task<Result> PushAsync(string localPath, string remotePath, CancellationToken ct = default)
        {
            PushCount++;
            LastPushLocalPath = localPath;
            LastPushRemotePath = remotePath;
            return Task.FromResult(_pushResult);
        }

        public Task<Result<IReadOnlyList<ThorFileEntry>>> ListAsync(string remotePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Result<ThorFileEntry>> StatAsync(string remotePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Result> DeleteAsync(string remotePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Result> MakeDirAsync(string remotePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Result> RenameAsync(string fromRemotePath, string toRemotePath, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private FileCache NewCache(FakeGateway gateway) => new(gateway, _baseDir);

    [Fact]
    public void Cache_directory_is_created_under_the_base_temp_dir()
    {
        using var cache = NewCache(new FakeGateway());

        Assert.StartsWith(_baseDir, cache.CacheDirectory);
        Assert.True(Directory.Exists(cache.CacheDirectory));
    }

    [Fact]
    public void Local_path_is_deterministic_for_the_same_remote_path()
    {
        using var cache = NewCache(new FakeGateway());

        var first = cache.LocalPathFor("/sdcard/Download/report.txt");
        var second = cache.LocalPathFor("/sdcard/Download/report.txt");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Distinct_remote_paths_map_to_distinct_local_files()
    {
        using var cache = NewCache(new FakeGateway());

        var a = cache.LocalPathFor("/sdcard/a/notes.txt");
        var b = cache.LocalPathFor("/sdcard/b/notes.txt");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Local_path_stays_inside_the_cache_directory_no_traversal()
    {
        using var cache = NewCache(new FakeGateway());

        var local = cache.LocalPathFor("/../../etc/passwd");

        Assert.Equal(cache.CacheDirectory, Path.GetDirectoryName(local));
        Assert.DoesNotContain(Path.DirectorySeparatorChar, Path.GetFileName(local));
    }

    [Fact]
    public void Local_path_for_blank_remote_path_throws()
    {
        using var cache = NewCache(new FakeGateway());
        Assert.Throws<ArgumentException>(() => cache.LocalPathFor("  "));
    }

    [Fact]
    public async Task EnsureCached_pulls_once_and_reuses_the_local_copy()
    {
        var gateway = new FakeGateway();
        using var cache = NewCache(gateway);

        var first = await cache.EnsureCachedAsync("/sdcard/a.txt");
        var second = await cache.EnsureCachedAsync("/sdcard/a.txt");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.True(cache.IsCached("/sdcard/a.txt"));
        Assert.Equal(1, gateway.PullCount);
    }

    [Fact]
    public async Task EnsureCached_surfaces_a_pull_failure_as_an_error()
    {
        var gateway = new FakeGateway(pull: Result.Failure(new Error("ADB_COMMAND_FAILED", "boom")));
        using var cache = NewCache(gateway);

        var result = await cache.EnsureCachedAsync("/sdcard/a.txt");

        Assert.False(result.IsSuccess);
        Assert.Equal("ADB_COMMAND_FAILED", result.Error!.Code);
        Assert.False(cache.IsCached("/sdcard/a.txt"));
    }

    [Fact]
    public async Task A_freshly_pulled_file_is_not_dirty_and_flush_does_not_push()
    {
        var gateway = new FakeGateway();
        using var cache = NewCache(gateway);

        await cache.EnsureCachedAsync("/sdcard/a.txt");
        Assert.False(cache.IsDirty("/sdcard/a.txt"));

        var flush = await cache.FlushAsync("/sdcard/a.txt");

        Assert.True(flush.IsSuccess);
        Assert.Equal(0, gateway.PushCount);
    }

    [Fact]
    public async Task MarkDirty_makes_flush_push_and_then_clears_the_flag()
    {
        var gateway = new FakeGateway();
        using var cache = NewCache(gateway);

        await cache.EnsureCachedAsync("/sdcard/a.txt");
        cache.MarkDirty("/sdcard/a.txt");
        Assert.True(cache.IsDirty("/sdcard/a.txt"));

        var flush = await cache.FlushAsync("/sdcard/a.txt");

        Assert.True(flush.IsSuccess);
        Assert.Equal(1, gateway.PushCount);
        Assert.Equal("/sdcard/a.txt", gateway.LastPushRemotePath);
        Assert.Equal(cache.LocalPathFor("/sdcard/a.txt"), gateway.LastPushLocalPath);
        Assert.False(cache.IsDirty("/sdcard/a.txt"));
    }

    [Fact]
    public async Task Flushing_twice_only_pushes_once()
    {
        var gateway = new FakeGateway();
        using var cache = NewCache(gateway);

        await cache.EnsureCachedAsync("/sdcard/a.txt");
        cache.MarkDirty("/sdcard/a.txt");

        await cache.FlushAsync("/sdcard/a.txt");
        await cache.FlushAsync("/sdcard/a.txt");

        Assert.Equal(1, gateway.PushCount);
    }

    [Fact]
    public async Task A_failed_push_keeps_the_file_dirty_for_a_retry()
    {
        var gateway = new FakeGateway(push: Result.Failure(new Error("ADB_COMMAND_FAILED", "offline")));
        using var cache = NewCache(gateway);

        await cache.EnsureCachedAsync("/sdcard/a.txt");
        cache.MarkDirty("/sdcard/a.txt");

        var flush = await cache.FlushAsync("/sdcard/a.txt");

        Assert.False(flush.IsSuccess);
        Assert.True(cache.IsDirty("/sdcard/a.txt"));
    }

    [Fact]
    public void CreateLocal_makes_an_empty_dirty_file()
    {
        var gateway = new FakeGateway();
        using var cache = NewCache(gateway);

        var result = cache.CreateLocal("/sdcard/new.txt");

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.Value));
        Assert.True(cache.IsDirty("/sdcard/new.txt"));
        Assert.True(cache.IsCached("/sdcard/new.txt"));
    }

    [Fact]
    public async Task Evict_drops_the_entry_and_its_local_file()
    {
        var gateway = new FakeGateway();
        using var cache = NewCache(gateway);

        await cache.EnsureCachedAsync("/sdcard/a.txt");
        var local = cache.LocalPathFor("/sdcard/a.txt");
        Assert.True(File.Exists(local));

        cache.Evict("/sdcard/a.txt");

        Assert.False(cache.IsCached("/sdcard/a.txt"));
        Assert.False(File.Exists(local));
    }

    [Fact]
    public async Task Dispose_deletes_the_whole_cache_directory()
    {
        var gateway = new FakeGateway();
        var cache = NewCache(gateway);
        await cache.EnsureCachedAsync("/sdcard/a.txt");
        var dir = cache.CacheDirectory;
        Assert.True(Directory.Exists(dir));

        cache.Dispose();

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var cache = NewCache(new FakeGateway());

        cache.Dispose();
        cache.Dispose();
    }

    [Fact]
    public async Task Operations_after_dispose_throw_object_disposed()
    {
        var cache = NewCache(new FakeGateway());
        cache.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => cache.EnsureCachedAsync("/sdcard/a.txt"));
    }
}
