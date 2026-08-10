namespace AynThorManager.Core.DTOs;

/// <summary>
/// Response DTO for directory listing operations.
/// </summary>
/// <param name="Entries">List of file and directory entries.</param>
/// <param name="Path">The listed directory path.</param>
/// <param name="IsTruncated">Whether the results were truncated (more than 1000 entries).</param>
/// <param name="TotalCount">Total number of entries in the directory.</param>
public sealed record ListDirectoryResponseDto(
    IReadOnlyList<FileEntryDto> Entries,
    string Path,
    bool IsTruncated,
    int TotalCount);
