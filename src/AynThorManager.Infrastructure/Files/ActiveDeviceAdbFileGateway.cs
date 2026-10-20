using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Files;

public sealed class ActiveDeviceAdbFileGateway : IAdbFileGateway
{
    private readonly IAdbCommandExecutor _executor;
    private readonly Func<string?> _serialProvider;
    private readonly object _gate = new();

    private IAdbFileGateway? _inner;

    public ActiveDeviceAdbFileGateway(IAdbCommandExecutor executor, Func<string?> serialProvider)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(serialProvider);

        _executor = executor;
        _serialProvider = serialProvider;
    }

    public Task<Result<IReadOnlyList<ThorFileEntry>>> ListAsync(string remotePath, CancellationToken ct = default) =>
        Run(g => g.ListAsync(remotePath, ct), Result<IReadOnlyList<ThorFileEntry>>.Failure);

    public Task<Result<ThorFileEntry>> StatAsync(string remotePath, CancellationToken ct = default) =>
        Run(g => g.StatAsync(remotePath, ct), Result<ThorFileEntry>.Failure);

    public Task<Result> PullAsync(string remotePath, string localPath, CancellationToken ct = default) =>
        Run(g => g.PullAsync(remotePath, localPath, ct));

    public Task<Result> PushAsync(string localPath, string remotePath, CancellationToken ct = default) =>
        Run(g => g.PushAsync(localPath, remotePath, ct));

    public Task<Result> DeleteAsync(string remotePath, CancellationToken ct = default) =>
        Run(g => g.DeleteAsync(remotePath, ct));

    public Task<Result> MakeDirAsync(string remotePath, CancellationToken ct = default) =>
        Run(g => g.MakeDirAsync(remotePath, ct));

    public Task<Result> RenameAsync(string fromRemotePath, string toRemotePath, CancellationToken ct = default) =>
        Run(g => g.RenameAsync(fromRemotePath, toRemotePath, ct));

    private Task<T> Run<T>(Func<IAdbFileGateway, Task<T>> call, Func<Error, T> fail)
    {
        if (!TryResolve(out var gateway, out var error))
            return Task.FromResult(fail(error));

        return call(gateway);
    }

    private Task<Result> Run(Func<IAdbFileGateway, Task<Result>> call)
    {
        if (!TryResolve(out var gateway, out var error))
            return Task.FromResult(Result.Failure(error));

        return call(gateway);
    }

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
