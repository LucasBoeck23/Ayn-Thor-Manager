namespace AynThorManager.Core.Models;

public enum FileEntryType
{
    File,
    Directory
}

public sealed record FileEntry(
    string Name,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    FileEntryType Type);
