using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Files;
using CommandOutput = AynThorManager.Infrastructure.Streaming.CommandOutput;

namespace AynThorManager.Infrastructure.Mount;

/// <summary>
/// The Infrastructure implementation of <see cref="IThorMount"/> on top of the native
/// FUSE→ADB translator (<see cref="ThorFuseFilesystem"/>) instead of a third-party
/// <c>adbfs</c> binary. It owns the moving parts of a live mount: the per-user mount
/// point, the dedicated thread that runs the blocking libfuse loop, and the
/// <c>fusermount</c> unmount (with a lazy fallback). It is the class that replaces the old
/// <c>AdbfsMount</c>; the host-side prerequisite detection it used to also carry now lives
/// in a dedicated <c>IMountPrerequisites</c> (task 3.5).
///
/// Lifecycle of a mount:
/// <list type="number">
///   <item><description>
///     <see cref="MountAsync"/> resolves a per-user mount point (Req 1.4 — a subfolder of
///     <c>$XDG_RUNTIME_DIR</c> when set, otherwise <c>~/.local/state/ayn-thor-manager/mnt</c>),
///     builds the <see cref="FuseOperations"/> table from the translator, pins it for the
///     mount's lifetime with a <see cref="FuseOperationsHandle"/>, and starts a dedicated
///     thread that calls <c>fuse_main_real</c>. That native call blocks until the
///     filesystem is unmounted, so it must not run on a pooled/worker thread.
///   </description></item>
///   <item><description>
///     <see cref="UnmountAsync"/> runs <c>fusermount -u</c> and, when that fails, falls
///     back to a lazy <c>fusermount -u -z</c> (Req 4.3) so the file manager is never left
///     stuck on a dead folder. The successful unmount makes <c>fuse_main_real</c> return,
///     which lets the loop thread finish; we then release the pinned operations and clear
///     the per-mount cache in <c>/tmp</c> (Req 4.4).
///   </description></item>
/// </list>
///
/// Both operations are idempotent (Req 1.3 / 4.3 and the design's Property 2): mounting
/// when something is already mounted is a no-op, and unmounting when nothing is mounted is
/// a no-op. The ADB serial is handed to the active <see cref="IAdbFileGateway"/> the
/// translator talks through — the Thor the app already connected — so no IP is ever asked
/// of the user (Req 1.2); the <paramref name="serial"/> argument lets a caller assert it is
/// mounting the active device.
///
/// Command execution goes through an injectable seam (<see cref="FusermountRunner"/>) so
/// the unmount logic — in particular the normal → lazy fallback — can be driven with fake
/// command outputs without spawning real processes, matching the pattern used by the other
/// Infrastructure services.
/// </summary>
public sealed class ThorFuseMount : IThorMount, IDisposable
{
    /// <summary>Candidate names of the FUSE mount helper, checked in order (fuse3 first).</summary>
    private static readonly string[] FusermountBinaries = ["fusermount3", "fusermount"];

    private readonly ThorFuseFilesystem _filesystem;
    private readonly FileCache _cache;
    private readonly FusermountRunner _run;

    private readonly object _lock = new();

    // The state of the single in-flight mount, or null when nothing is mounted. Guarded by
    // _lock; the loop thread and the caller of Mount/Unmount touch it from different
    // threads.
    private MountState? _state;
    private int _disposed;

    /// <summary>Drives <c>fusermount</c> against the real system via CliWrap.</summary>
    public ThorFuseMount(ThorFuseFilesystem filesystem, FileCache cache)
        : this(filesystem, cache, RealRun)
    {
    }

    /// <summary>
    /// Test seam: injects how <c>fusermount</c> is executed, so the unmount fallback can
    /// be driven with canned outputs without spawning real processes.
    /// </summary>
    internal ThorFuseMount(ThorFuseFilesystem filesystem, FileCache cache, FusermountRunner run)
    {
        ArgumentNullException.ThrowIfNull(filesystem);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(run);

        _filesystem = filesystem;
        _cache = cache;
        _run = run;
    }

    /// <inheritdoc />
    public bool IsMounted
    {
        get { lock (_lock) return _state is not null; }
    }

    /// <inheritdoc />
    public string? MountPoint
    {
        get { lock (_lock) return _state?.MountPoint; }
    }

    /// <summary>
    /// Req 1.1/1.4: mounts the Thor at a per-user mount point by bringing up the native
    /// FUSE loop on a dedicated thread. Idempotent — if a mount is already live this is a
    /// no-op (Req 1.3 / Property 2). The <paramref name="serial"/> identifies the active
    /// ADB device; the translator's gateway already targets that device, so the serial is
    /// only validated here, not re-plumbed. Throws <see cref="InvalidOperationException"/>
    /// when the FUSE loop cannot establish the mount (e.g. <c>libfuse</c> absent, mount
    /// point unusable).
    /// </summary>
    public Task MountAsync(string serial, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A serial/endereço ADB do Thor é obrigatório.", nameof(serial));

        lock (_lock)
        {
            // Already mounted: do not mount again (Req 1.3). The caller reopens the file
            // manager on the existing mount point.
            if (_state is not null)
                return Task.CompletedTask;
        }

        var mountPoint = ResolveMountPoint();
        Directory.CreateDirectory(mountPoint);

        // Pin the callback table for as long as the mount runs — libfuse keeps raw
        // function pointers into these delegates and fuse_main_real blocks until unmount,
        // so the GC must not reclaim them meanwhile (Property 6).
        var operations = _filesystem.CreateOperations();
        var handle = new FuseOperationsHandle(operations);

        // argv for fuse_main_real: program name, the mount point, and the options.
        //  -f  foreground: fuse_main_real blocks on this thread instead of daemonizing, so
        //      the dedicated loop thread owns the blocking call and a clean `fusermount -u`
        //      is what makes it return.
        //  -s  single-threaded: libfuse dispatches one request at a time. Our callbacks are
        //      synchronous and each blocks on a (serial) ADB command through a shared cache
        //      and gateway; multi-threaded dispatch raced those and produced flaky ENOENT/
        //      EACCES. ADB is serial anyway, so serializing the callbacks costs nothing and
        //      makes every operation deterministic.
        var argv = new[] { "ayn-thor-manager", mountPoint, "-f", "-s" };

        // Surface a mount failure (fuse_main_real returning non-zero) back to the caller
        // of MountAsync, which runs before the loop would block.
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
                // A DllNotFoundException (libfuse missing) or any marshalling fault must
                // not crash the process; report it as a failed mount.
                ready.TrySetException(ex);
                return;
            }

            // fuse_main_real only returns once the filesystem is torn down. A non-zero exit
            // observed before anyone reported "ready" means the mount never came up.
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

        return WaitUntilMountedOrFailedAsync(state, ct);
    }

    /// <summary>
    /// Waits briefly to catch an immediate mount failure (libfuse missing, mount point
    /// unusable) and surface it as an exception, while treating the normal case — the loop
    /// thread blocking inside <c>fuse_main_real</c> — as a successful mount. If the loop
    /// faults or exits during the grace window, the mount is torn down and the error is
    /// rethrown so the caller can report it and retry (Req 3.3 / design error handling).
    /// </summary>
    private async Task WaitUntilMountedOrFailedAsync(MountState state, CancellationToken ct)
    {
        // The loop blocks for the whole mount, so "ready" completing within the grace
        // window can only mean it failed to establish. A timeout here means the mount is
        // up (the thread is blocked serving requests).
        var settled = await Task.WhenAny(state.Ready.Task, Task.Delay(MountGrace, ct)).ConfigureAwait(false);

        if (settled != state.Ready.Task)
            return; // Still blocking inside fuse_main_real → mounted.

        // The loop returned already: the mount could not be established. Clean up and
        // surface the failure.
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

    /// <summary>
    /// Req 4.2/4.3: unmounts the Thor if it is mounted. Idempotent — safe to call when
    /// nothing is mounted. Tries a normal <c>fusermount -u</c> first; when that fails it
    /// falls back to a lazy unmount (<c>fusermount -u -z</c>) so the file manager is never
    /// left stuck on a dead folder. The successful unmount makes <c>fuse_main_real</c>
    /// return; the loop thread is then joined and the per-mount cache in <c>/tmp</c> is
    /// cleared (Req 4.4).
    /// </summary>
    public async Task UnmountAsync(CancellationToken ct = default)
    {
        MountState? state;
        lock (_lock)
        {
            state = _state;
            // Detach eagerly so a second concurrent unmount is a no-op and IsMounted flips
            // immediately.
            _state = null;
        }

        if (state is null)
            return;

        var fusermount = await ResolveFusermountAsync(ct).ConfigureAwait(false) ?? FusermountBinaries[^1];

        // Normal unmount; on failure fall back to a lazy unmount (Req 4.3). The lazy
        // unmount detaches the filesystem immediately even if something still holds it
        // open, so we never leave an orphan mount behind.
        var normal = await _run(fusermount, ["-u", state.MountPoint], ct).ConfigureAwait(false);
        if (normal.ExitCode != 0)
            await _run(fusermount, ["-u", "-z", state.MountPoint], ct).ConfigureAwait(false);

        TearDown(state);
    }

    /// <summary>
    /// Releases the resources of a finished mount: waits for the loop thread to observe the
    /// unmount and exit, frees the pinned operations handle, and clears the per-mount cache
    /// (Req 4.4). Best-effort and safe to call more than once for the same state.
    /// </summary>
    private void TearDown(MountState state)
    {
        // Give the loop thread a bounded window to return from fuse_main_real after the
        // unmount; it is a background thread, so a stuck join never blocks shutdown.
        if (state.Thread.IsAlive)
            state.Thread.Join(ThreadJoinTimeout);

        state.Handle.Dispose();

        // Clear the /tmp cache for this mount so nothing is left orphaned (Req 4.4).
        _cache.Dispose();
    }

    /// <summary>
    /// Disposes the mount: best-effort synchronous unmount so the window close path never
    /// leaves an orphan (Req 4.2). Idempotent.
    /// </summary>
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
            // Best-effort on the close path: never throw out of Dispose. Any residual
            // mount is a lazy-unmount away and the cache is cleared regardless.
        }
    }

    /// <summary>
    /// Resolves the fusermount binary name to use for unmounting: whichever of
    /// <see cref="FusermountBinaries"/> is on PATH. Returns <c>null</c> when none is found
    /// (the caller then falls back to a default name for a best-effort attempt).
    /// </summary>
    private async Task<string?> ResolveFusermountAsync(CancellationToken ct)
    {
        foreach (var candidate in FusermountBinaries)
        {
            var result = await _run("which", [candidate], ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Resolves the per-user mount point (Req 1.4): a subfolder of <c>$XDG_RUNTIME_DIR</c>
    /// when that is set (the natural place for ephemeral per-user runtime state), otherwise
    /// <c>~/.local/state/ayn-thor-manager/mnt</c>.
    /// </summary>
    private static string ResolveMountPoint()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDir))
            return Path.Combine(runtimeDir, "ayn-thor-manager", "mnt");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "state", "ayn-thor-manager", "mnt");
    }

    /// <summary>
    /// Runs a command via CliWrap and returns its outcome. Never throws: a launch failure
    /// is reported as a sentinel exit code so the unmount fallback surfaces cleanly instead
    /// of crashing. Mirrors the shared helper used by the other Infrastructure services.
    /// </summary>
    private static async Task<CommandOutput> RealRun(
        string file,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        try
        {
            var result = await Cli.Wrap(file)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            return new CommandOutput(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (Exception ex)
        {
            return new CommandOutput(-1, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// How long <see cref="MountAsync"/> waits to catch an immediate mount failure before
    /// treating the still-blocking loop as a successful mount.
    /// </summary>
    private static readonly TimeSpan MountGrace = TimeSpan.FromMilliseconds(500);

    /// <summary>Bounded wait for the loop thread to exit after an unmount.</summary>
    private static readonly TimeSpan ThreadJoinTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The live state of a single mount: its mount point, the dedicated thread running the
    /// blocking FUSE loop, the handle pinning the callbacks, and the signal the loop sets
    /// if it exits (used to detect an immediate mount failure).
    /// </summary>
    private sealed record MountState(
        string MountPoint,
        Thread Thread,
        FuseOperationsHandle Handle,
        TaskCompletionSource<int> Ready);
}

/// <summary>
/// Executes <c>fusermount</c>/<c>which</c> and returns its outcome. The mount flow needs an
/// injectable command seam so the unmount fallback (normal → lazy) can be driven with
/// canned outputs in tests without spawning real processes.
/// </summary>
internal delegate Task<CommandOutput> FusermountRunner(
    string file,
    IReadOnlyList<string> args,
    CancellationToken ct);
