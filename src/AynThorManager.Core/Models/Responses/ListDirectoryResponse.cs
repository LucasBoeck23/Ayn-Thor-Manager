namespace AynThorManager.Core.Models.Responses;

public sealed record ListDirectoryResponse(
    IReadOnlyList<FileEntry> Entries,
    string Path,
    bool IsTruncated,
    int TotalCount);
