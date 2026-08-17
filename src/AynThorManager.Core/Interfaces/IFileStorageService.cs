using AynThorManager.Core.Models;
using AynThorManager.Core.Models.Responses;

namespace AynThorManager.Core.Interfaces;

public interface IFileStorageService
{
    Task<Result<ListDirectoryResponse>> ListDirectoryAsync(string path, CancellationToken ct);

    Task<Result<CreateDirectoryResponse>> CreateDirectoryAsync(string parentPath, string name, CancellationToken ct);

    Task<Result<RenameResponse>> RenameAsync(string currentPath, string newName, CancellationToken ct);

    Task<Result<DeleteResponse>> DeleteAsync(string path, CancellationToken ct);
}
