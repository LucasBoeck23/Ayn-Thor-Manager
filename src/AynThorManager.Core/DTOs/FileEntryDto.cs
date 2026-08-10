namespace AynThorManager.Core.DTOs;

/// <summary>
/// DTO representing a single file or directory entry.
/// </summary>
/// <param name="Name">File or directory name.</param>
/// <param name="SizeBytes">Size in bytes.</param>
/// <param name="ModifiedAt">Last modification date in ISO 8601 format.</param>
/// <param name="Type">Entry type: "file" or "directory".</param>
public sealed record FileEntryDto(
    string Name,
    long SizeBytes,
    string ModifiedAt,
    string Type);
