using AynThorManager.Core.Models;
using AynThorManager.Core.Models.Responses;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// CRUD operations on files and directories in the device storage.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Lists the contents of a directory on the device.
    /// Returns entries sorted by type (directories first) then alphabetically.
    /// Truncates at 1000 entries.
    /// </summary>
    /// <param name="path">Absolute path on the device.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing directory listing or an error.</returns>
    Task<Result<ListDirectoryResponse>> ListDirectoryAsync(string path, CancellationToken ct);

    /// <summary>
    /// Creates a new directory on the device.
    /// </summary>
    /// <param name="parentPath">Parent directory path on the device.</param>
    /// <param name="name">Name for the new directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the created directory info or an error.</returns>
    Task<Result<CreateDirectoryResponse>> CreateDirectoryAsync(string parentPath, string name, CancellationToken ct);

    /// <summary>
    /// Renames a file or directory on the device.
    /// </summary>
    /// <param name="currentPath">Current absolute path of the item.</param>
    /// <param name="newName">New name for the item (name only, no path separators).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the rename info or an error.</returns>
    Task<Result<RenameResponse>> RenameAsync(string currentPath, string newName, CancellationToken ct);

    /// <summary>
    /// Deletes a file or directory (recursively) on the device.
    /// </summary>
    /// <param name="path">Absolute path of the item to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the deletion info or an error.</returns>
    Task<Result<DeleteResponse>> DeleteAsync(string path, CancellationToken ct);
}
