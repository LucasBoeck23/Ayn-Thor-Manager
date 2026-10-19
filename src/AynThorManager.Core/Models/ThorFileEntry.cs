namespace AynThorManager.Core.Models;

/// <summary>
/// What kind of filesystem node a <see cref="ThorFileEntry"/> is, as parsed from the
/// leading type character of <c>ls -la</c> (<c>-</c> file, <c>d</c> directory,
/// <c>l</c> symlink). Anything else (block/char/socket/fifo) maps to <see cref="Other"/>.
/// </summary>
public enum ThorFileType
{
    File,
    Directory,
    Symlink,
    Other
}

/// <summary>
/// One entry from a Thor directory listing, the parsed form of a single <c>ls -la</c>
/// line. Internal to the ADB file gateway: it carries only the metadata the FUSE
/// translator needs to answer <c>getattr</c>/<c>readdir</c> lazily, without transferring
/// any file content.
/// </summary>
/// <param name="Name">The entry's name (no path, no trailing slash).</param>
/// <param name="Size">Size in bytes (0 for directories/special nodes).</param>
/// <param name="Type">Whether this is a file, directory, symlink or other node.</param>
/// <param name="ModifiedTime">Last-modified time parsed from the listing, or <c>null</c> when it could not be parsed.</param>
/// <param name="Permissions">The raw permission string from the listing (e.g. <c>rwxr-xr-x</c>), without the leading type character.</param>
public sealed record ThorFileEntry(
    string Name,
    long Size,
    ThorFileType Type,
    DateTimeOffset? ModifiedTime,
    string Permissions);
