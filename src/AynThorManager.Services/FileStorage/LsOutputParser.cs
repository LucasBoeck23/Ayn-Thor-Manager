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

        // Skip blank/short lines and the "total NN" header ls prints first.
        if (trimmed.Length < 10 || trimmed.StartsWith("total", StringComparison.OrdinalIgnoreCase))
            return null;

        // First char is the entry type: d=dir, -=file, l=symlink. Anything else
        // is not a listing row.
        var typeChar = trimmed[0];
        if (typeChar is not ('d' or '-' or 'l'))
            return null;

        // A valid `ls -la` row has at least 8 columns (perms..name).
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 8)
            return null;

        var name = string.Join(' ', parts[7..]);

        // Current/parent dir entries are not shown in the UI.
        if (name is "." or "..")
            return null;

        // Symlinks are listed as "name -> target"; keep only the link name.
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
