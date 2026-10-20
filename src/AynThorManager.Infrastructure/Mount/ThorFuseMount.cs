using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Files;
using AynThorManager.Infrastructure.Processes;

namespace AynThorManager.Infrastructure.Mount;

public sealed class ThorFuseMount : IThorMount, IDisposable
{
    private static readonly string[] FusermountBinaries = ["fusermount3", "fusermount"];

    private readonly ThorFuseFilesystem _filesystem;
    private readonly FileCache _cache;
    private readonly CommandRunner _run;

    private readonly object _lock = new();

    private MountState? _state;
    private int _disposed;

    public ThorFuseMount(ThorFuseFilesystem filesystem, FileCache cache)
        : this(filesystem, cache, ProcessRunner.RunAsync)
    {
    }

    internal ThorFuseMount(ThorFuseFilesystem filesystem, FileCache cache, CommandRunner run)
    {
        ArgumentNullException.ThrowIfNull(filesystem);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(run);

        _filesystem = filesystem;
        _cache = cache;
        _run = run;
    }

    public bool IsMounted
    {
        get { lock (_lock) return _state is not null; }
    }

    public string? MountPoint
    {
        get { lock (_lock) return _state?.MountPoint; }
    }

    public async Task MountAsync(string serial, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A serial/endereço ADB do Thor é obrigatório.", nameof(serial));

        lock (_lock)
        {
            if (_state is not null)
                return;
        }

        var mountPoint = ResolveMountPoint();
        await ClearOrphanMountAsync(mountPoint, ct).ConfigureAwait(false);
        Directory.CreateDirectory(mountPoint);

        var operations = _filesystem.CreateOperations();
        var handle = new FuseOperationsHandle(operations);

        var argv = new[] { "ayn-thor-manager", mountPoint, "-f", "-s" };

        var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            int exit;
            try
            {
                exit = LibFuseInterop.FuseMain(
                    argv, ref operations, LibFuseInterop.OperationsSize, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
                return;
            }

            ready.TrySetResult(exit);
        })
        {
            IsBackground = true,
            Name = "thor-fuse-loop",
        };

        var state = new MountState(mountPoint, thread, handle, ready);

        lock (_lock)
        {
            _state = state;
        }

        thread.Start();

        await WaitUntilMountedOrFailedAsync(state, ct).ConfigureAwait(false);
    }

    private async Task WaitUntilMountedOrFailedAsync(MountState state, CancellationToken ct)
    {
        var settled = await Task.WhenAny(state.Ready.Task, Task.Delay(MountGrace, ct)).ConfigureAwait(false);

        if (settled != state.Ready.Task)
            return;

        Exception? failure = null;
        int exit = 0;
        try
        {
            exit = await state.Ready.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        TearDown(state);

        if (failure is not null)
            throw new InvalidOperationException(
                $"Falha ao montar o Thor via FUSE em \"{state.MountPoint}\": {failure.Message}", failure);

        throw new InvalidOperationException(
            $"Falha ao montar o Thor via FUSE em \"{state.MountPoint}\" (código {exit}).");
    }

    public async Task UnmountAsync(CancellationToken ct = default)
    {
        MountState? state;
        lock (_lock)
        {
            state = _state;
            _state = null;
        }

        if (state is null)
            return;

        var fusermount = await ResolveFusermountAsync(ct).ConfigureAwait(false) ?? FusermountBinaries[^1];

        var normal = await _run(fusermount, ["-u", state.MountPoint], ct).ConfigureAwait(false);
        if (normal.ExitCode != 0)
            await _run(fusermount, ["-u", "-z", state.MountPoint], ct).ConfigureAwait(false);

        TearDown(state);
    }

    private void TearDown(MountState state)
    {
        if (state.Thread.IsAlive)
            state.Thread.Join(ThreadJoinTimeout);

        state.Handle.Dispose();

        _cache.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            UnmountAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private async Task ClearOrphanMountAsync(string mountPoint, CancellationToken ct)
    {
        if (!IsOrphanMount(mountPoint))
            return;

        var fusermount = await ResolveFusermountAsync(ct).ConfigureAwait(false) ?? FusermountBinaries[^1];
        await _run(fusermount, ["-u", "-z", mountPoint], ct).ConfigureAwait(false);
    }

    private static bool IsOrphanMount(string mountPoint)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ');
                if (parts.Length >= 2 && parts[1] == mountPoint)
                    return true;
            }
        }
        catch (IOException)
        {
        }

        return false;
    }

    private Task<string?> ResolveFusermountAsync(CancellationToken ct) =>
        ProcessRunner.WhichAsync(_run, FusermountBinaries, ct);

    private static string ResolveMountPoint()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDir))
            return Path.Combine(runtimeDir, "ayn-thor-manager", "mnt");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "state", "ayn-thor-manager", "mnt");
    }

    private static readonly TimeSpan MountGrace = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan ThreadJoinTimeout = TimeSpan.FromSeconds(5);

    private sealed record MountState(
        string MountPoint,
        Thread Thread,
        FuseOperationsHandle Handle,
        TaskCompletionSource<int> Ready);
}
