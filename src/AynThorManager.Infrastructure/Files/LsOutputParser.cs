using System.Globalization;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Files;

/// <summary>
/// Pure parser for Android/toybox <c>ls -la</c> output into <see cref="ThorFileEntry"/>.
/// Kept separate from <see cref="AdbFileGateway"/> and free of any I/O so the parsing
/// rules — the first unit-testable point of the file gateway — can be driven with canned
/// listing text, mirroring how <c>AdbOutputParser</c> stays a pure helper.
///
/// A typical line from the Thor looks like:
/// <code>-rw-rw---- 1 u0_a123 sdcard_rw 12345 2024-01-15 13:45 report.txt</code>
/// so the columns are: mode (type char + 9 permission chars), link count, owner, group,
/// size, date, time, then the name (which may contain spaces). A symlink carries a
/// <c>name -&gt; target</c> suffix, of which only the link's own name is kept. Entries for
/// <c>.</c> and <c>..</c> and the leading <c>total N</c> header are dropped — the FUSE
/// translator only wants the real children.
/// </summary>
internal static class LsOutputParser
{
    // toybox ls -la uses "YYYY-MM-DD HH:MM" for the mtime; parse it as local time
    // (the Thor's clock) and fall back to null when the columns don't line up.
    private static readonly string[] DateTimeFormats =
    {
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
    };

    /// <summary>
    /// Parses the full <c>ls -la</c> <paramref name="output"/> into entries, skipping the
    /// <c>total</c> header, the <c>.</c>/<c>..</c> pseudo-entries, and any line that does
    /// not have the expected shape. Never throws.
    /// </summary>
    public static IReadOnlyList<ThorFileEntry> Parse(string output)
    {
        var entries = new List<ThorFileEntry>();
        if (string.IsNullOrEmpty(output))
            return entries;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0)
                continue;

            // "total 48" header emitted by ls -la — not an entry.
            if (line.StartsWith("total ", StringComparison.Ordinal))
                continue;

            var entry = ParseLine(line);
            if (entry is null)
                continue;

            if (entry.Name is "." or "..")
                continue;

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Parses a single listing line, or returns <c>null</c> when it is not a recognizable
    /// <c>ls -la</c> entry (too few columns, or no leading mode block).
    /// </summary>
    public static ThorFileEntry? ParseLine(string line)
    {
        // Split off the leading, fixed columns: mode, links, owner, group, size, date,
        // time. Everything after the 7th column is the name (which may contain spaces),
        // so we cap the split and keep the remainder intact.
        var columns = line.Split((char[]?)null, 8, StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length < 8)
            return null;

        var mode = columns[0];
        if (mode.Length < 10)
            return null;

        var type = ClassifyType(mode[0]);
        var permissions = mode[1..];

        if (!long.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            size = 0;

        var modifiedTime = ParseModifiedTime(columns[5], columns[6]);

        var name = NormalizeName(columns[7], type);
        if (name.Length == 0)
            return null;

        return new ThorFileEntry(name, size, type, modifiedTime, permissions);
    }

    /// <summary>Maps the leading mode character to a <see cref="ThorFileType"/>.</summary>
    private static ThorFileType ClassifyType(char typeChar) => typeChar switch
    {
        '-' => ThorFileType.File,
        'd' => ThorFileType.Directory,
        'l' => ThorFileType.Symlink,
        _ => ThorFileType.Other,
    };

    /// <summary>
    /// Keeps only the link's own name for symlinks, dropping the <c>-&gt; target</c>
    /// suffix; returns other names unchanged.
    /// </summary>
    private static string NormalizeName(string rawName, ThorFileType type)
    {
        if (type == ThorFileType.Symlink)
        {
            var arrow = rawName.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                return rawName[..arrow].TrimEnd();
        }

        return rawName.TrimEnd();
    }

    /// <summary>
    /// Parses the "YYYY-MM-DD" + "HH:MM" columns into a <see cref="DateTimeOffset"/> in the
    /// local timezone, or <c>null</c> when they don't match the expected shape.
    /// </summary>
    private static DateTimeOffset? ParseModifiedTime(string date, string time)
    {
        var stamp = $"{date} {time}";
        if (DateTime.TryParseExact(
                stamp,
                DateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed))
        {
            return new DateTimeOffset(parsed);
        }

        return null;
    }
}
