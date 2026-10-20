using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

public interface IAdbFileGateway
{
    Task<Result<IReadOnlyList<ThorFileEntry>>> ListAsync(string remotePath, CancellationToken ct = default);

    Task<Result<ThorFileEntry>> StatAsync(string remotePath, CancellationToken ct = default);

    Task<Result> PullAsync(string remotePath, string localPath, CancellationToken ct = default);

    Task<Result> PushAsync(string localPath, string remotePath, CancellationToken ct = default);

    Task<Result> DeleteAsync(string remotePath, CancellationToken ct = default);

    Task<Result> MakeDirAsync(string remotePath, CancellationToken ct = default);

    Task<Result> RenameAsync(string fromRemotePath, string toRemotePath, CancellationToken ct = default);
}
