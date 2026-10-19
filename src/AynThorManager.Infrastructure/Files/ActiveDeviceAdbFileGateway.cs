using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Files;

/// <summary>
/// An <see cref="IAdbFileGateway"/> that binds to the active device serial lazily, on the
/// first file operation, instead of at construction.
///
/// Why this exists (the composition seam): <see cref="AdbFileGateway"/> is bound to ONE
/// device serial for its whole life (it prefixes every command with <c>-s {serial}</c>,
/// Req 5.3), and its constructor demands a non-empty serial up front. But the whole FUSE
/// object graph (gateway → <see cref="FileCache"/> → <c>ThorFuseFilesystem</c> →
/// <c>ThorFuseMount</c>) is built once, at app startup, when no Thor is connected yet — the
/// serial only becomes known at mount time, from <c>DeviceConnection.Current?.Serial</c>,
/// which the Services orchestrator reads just before it calls
/// <see cref="IThorMount.MountAsync(string, CancellationToken)"/>.
///
/// The native FUSE mount never touches the gateway before it is mounted, and a mount only
/// happens after a Thor is connected. So resolving the serial on first use is both correct
/// and the point where it is first knowable. This wrapper resolves it once (the first file
/// operation after a mount), builds the real <see cref="AdbFileGateway"/> bound to that
/// serial, and delegates every call to it from then on.
///
/// This keeps the Core contracts and the Services layer untouched: the orchestrator still
/// sees a single <see cref="IThorMount"/>/<see cref="IMountPrerequisites"/>, the
/// <see cref="AdbFileGateway"/> constructor stays <c>(executor, serial)</c>, and the
/// serial-binding timing problem is solved entirely in the composition/Infrastructure
/// layer where the concrete wiring belongs.
/// </summary>
public sealed class ActiveDeviceAdbFileGateway : IAdbFileGateway
{
    private readonly IAdbCommandExecutor _executor;
    private readonly Func<string?> _serialProvider;
    private readonly object _gate = new();

    private IAdbFileGateway? _inner;

    /// <summary>
    /// Builds the lazy gateway over the shared <paramref name="executor"/> (the same
    /// <see cref="IAdbCommandExecutor"/> the rest of the app uses) and a
    /// <paramref name="serialProvider"/> that reads the active device serial when it is
    /// needed — e.g. <c>() =&gt; deviceConnection.Current?.Serial</c>.
    /// </summary>
    public ActiveDeviceAdbFileGateway(IAdbCommandExecutor executor, Func<string?> serialProvider)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(serialProvider);

        _executor = executor;
        _serialProvider = serialProvider;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ThorFileEntry>>> ListAsync(string remotePath, CancellationToken ct = default)
    {
        if (!TryResolve(out var gateway, out var error))
            return Task.FromResult(Result<IReadOnlyList<ThorFileEntry>>.Failure(error));

        return gateway.ListAsync(remotePath, ct);
    }

    /// <inheritdoc />
    public Task<Result<ThorFileEntry>> StatAsync(string remotePath, CancellationToken ct = default)
    {
        if (!TryResolve(out var gateway, out var error))
            return Task.FromResult(Result<ThorFileEntry>.Failure(error));

        return gateway.StatAsync(remotePath, ct);
    }

    /// <inheritdoc />
    public Task<Result> PullAsync(string remotePath, string localPath, CancellationToken ct = default)
    {
        if (!TryResolve(out var gateway, out var error))
            return Task.FromResult(Result.Failure(error));

        return gateway.PullAsync(remotePath, localPath, ct);
    }

    /// <inheritdoc />
    public Task<Result> PushAsync(string localPath, string remotePath, CancellationToken ct = default)
    {
        if (!TryResolve(out var gateway, out var error))
            return Task.FromResult(Result.Failure(error));

        return gateway.PushAsync(localPath, remotePath, ct);
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(string remotePath, CancellationToken ct = default)
    {
        if (!TryResolve(out var gateway, out var error))
            return Task.FromResult(Result.Failure(error));

        return gateway.DeleteAsync(remotePath, ct);
    }

    /// <inheritdoc />
    public Task<Result> MakeDirAsync(string remotePath, CancellationToken ct = default)
    {
        if (!TryResolve(out var gateway, out var error))
            return Task.FromResult(Result.Failure(error));

        return gateway.MakeDirAsync(remotePath, ct);
    }

    /// <inheritdoc />
    public Task<Result> RenameAsync(string fromRemotePath, string toRemotePath, CancellationToken ct = default)
    {
        if (!TryResolve(out var gateway, out var error))
            return Task.FromResult(Result.Failure(error));

        return gateway.RenameAsync(fromRemotePath, toRemotePath, ct);
    }

    /// <summary>
    /// Returns the underlying <see cref="AdbFileGateway"/> bound to the active serial,
    /// building it on first use. If no Thor is connected (no serial), returns a clear
    /// <see cref="Error"/> so the FUSE callback maps it to an errno instead of crashing the
    /// mount — the same no-loose-exceptions contract the gateway itself keeps.
    ///
    /// The serial is resolved once and the real gateway is cached: once a mount is live the
    /// device does not change under it, and a dropped device tears the mount down (Req 4.1),
    /// so there is no in-flight re-binding to a different serial to worry about.
    /// </summary>
    private bool TryResolve(out IAdbFileGateway gateway, out Error error)
    {
        lock (_gate)
        {
            if (_inner is not null)
            {
                gateway = _inner;
                error = default!;
                return true;
            }

            var serial = _serialProvider();
            if (string.IsNullOrWhiteSpace(serial))
            {
                gateway = default!;
                error = new Error(
                    "DEVICE_NOT_CONNECTED",
                    "O Thor não está conectado; não há serial ADB ativo para as operações de arquivo.");
                return false;
            }

            _inner = new AdbFileGateway(_executor, serial);
            gateway = _inner;
            error = default!;
            return true;
        }
    }
}
