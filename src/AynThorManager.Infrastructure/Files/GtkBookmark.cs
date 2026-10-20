using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Settings;

namespace AynThorManager.Infrastructure.Files;

public sealed class GtkBookmark : ISidebarBookmark
{
    private const string FileScheme = "file://";

    private readonly string _bookmarksPath;

    public GtkBookmark() : this(ResolveBookmarksPath())
    {
    }

    internal GtkBookmark(string bookmarksPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarksPath);
        _bookmarksPath = bookmarksPath;
    }

    public async Task AddAsync(string path, string label, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var uri = ToFileUri(path);
            var line = string.IsNullOrWhiteSpace(label) ? uri : $"{uri} {label}";

            var lines = (await ReadLinesAsync(ct).ConfigureAwait(false)).ToList();

            if (lines.Any(existing => PointsAt(existing, uri)))
                return;

            lines.Add(line);

            await WriteLinesAsync(lines, ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async Task RemoveAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (!File.Exists(_bookmarksPath))
                return;

            var uri = ToFileUri(path);
            var lines = await ReadLinesAsync(ct).ConfigureAwait(false);
            var kept = lines.Where(existing => !PointsAt(existing, uri)).ToList();

            if (kept.Count == lines.Count)
                return;

            await WriteLinesAsync(kept, ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task<IReadOnlyList<string>> ReadLinesAsync(CancellationToken ct)
    {
        if (!File.Exists(_bookmarksPath))
            return [];

        var text = await File.ReadAllLinesAsync(_bookmarksPath, ct).ConfigureAwait(false);
        return text;
    }

    private async Task WriteLinesAsync(IReadOnlyList<string> lines, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_bookmarksPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(_bookmarksPath, lines, ct).ConfigureAwait(false);
    }

    private static bool PointsAt(string line, string uri)
    {
        var trimmed = line.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        var lineUri = spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];
        return string.Equals(lineUri, uri, StringComparison.Ordinal);
    }

    private static string ToFileUri(string path)
    {
        var full = Path.GetFullPath(path);
        return new UriBuilder(FileScheme + full).Uri.AbsoluteUri;
    }

    private static string ResolveBookmarksPath() =>
        Path.Combine(UserPaths.ConfigHome(), "gtk-3.0", "bookmarks");
}
