using System.Globalization;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Files;

internal static class LsOutputParser
{
    private static readonly string[] DateTimeFormats =
    {
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
    };

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

    public static ThorFileEntry? ParseLine(string line)
    {
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

    private static ThorFileType ClassifyType(char typeChar) => typeChar switch
    {
        '-' => ThorFileType.File,
        'd' => ThorFileType.Directory,
        'l' => ThorFileType.Symlink,
        _ => ThorFileType.Other,
    };

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
