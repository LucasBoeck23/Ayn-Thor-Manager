namespace AynThorManager.Core.Models;

public enum ThorFileType
{
    File,
    Directory,
    Symlink,
    Other
}

public sealed record ThorFileEntry(
    string Name,
    long Size,
    ThorFileType Type,
    DateTimeOffset? ModifiedTime,
    string Permissions);
