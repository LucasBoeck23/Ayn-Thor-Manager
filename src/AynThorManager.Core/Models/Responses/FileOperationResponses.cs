namespace AynThorManager.Core.Models.Responses;

public sealed record ListDirectoryResponse(
    IReadOnlyList<FileEntry> Entries,
    string Path,
    bool IsTruncated,
    int TotalCount);

public sealed record CreateDirectoryResponse(string FullPath);

public sealed record CreateFileResponse(string FullPath);

public sealed record UploadFileResponse(string FullPath);

public sealed record RenameResponse(string NewPath);

public sealed record DeleteResponse(string DeletedPath);
