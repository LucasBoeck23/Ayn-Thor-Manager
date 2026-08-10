namespace AynThorManager.Core.Models;

public sealed record FileEntry(
    string Name,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    FileEntryType Type);
