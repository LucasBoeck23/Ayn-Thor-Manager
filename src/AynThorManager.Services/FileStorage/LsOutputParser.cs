using System.Globalization;
using AynThorManager.Core.Models;

namespace AynThorManager.Services.FileStorage;

internal static class LsOutputParser
{
    public static List<FileEntry> Parse(string output)
    {
        var entries = new List<FileEntry>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var entry = TryParseLine(line);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    private static FileEntry? TryParseLine(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.Length < 10 || trimmed.StartsWith("total", StringComparison.OrdinalIgnoreCase))
            return null;

        var typeChar = trimmed[0];
        if (typeChar is not ('d' or '-' or 'l'))
            return null;

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 8)
            return null;

        var name = string.Join(' ', parts[7..]);

        if (name is "." or "..")
            return null;

        if (typeChar == 'l')
        {
            var arrowIndex = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
                name = name[..arrowIndex];
        }

        var entryType = typeChar == 'd' ? FileEntryType.Directory : FileEntryType.File;
        long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);

        var dateTimeStr = $"{parts[5]} {parts[6]}";
        if (!DateTimeOffset.TryParseExact(dateTimeStr, "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var modifiedAt))
        {
            modifiedAt = DateTimeOffset.MinValue;
        }

        return new FileEntry(name, size, modifiedAt, entryType);
    }
}
