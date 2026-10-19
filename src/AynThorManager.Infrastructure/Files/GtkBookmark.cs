using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Files;

/// <summary>
/// The Infrastructure implementation of <see cref="ISidebarBookmark"/> that edits the GTK
/// bookmarks file (<c>~/.config/gtk-3.0/bookmarks</c>) so the Thor mount point shows up as
/// a friendly "Ayn Thor" shortcut in the Nautilus/GTK sidebar (Req 1.5). Each bookmark is
/// one line of the form <c>file://&lt;path&gt; &lt;label&gt;</c>; adding appends the line
/// (when not already present) and removing drops it. The Services layer calls
/// <see cref="AddAsync"/> after mounting and <see cref="RemoveAsync"/> on unmount (Req 4.1).
///
/// Best-effort by design: v1 targets GTK/Nautilus only (Dolphin/KDE uses a different
/// mechanism). A missing file, a missing <c>~/.config/gtk-3.0</c> directory, or any IO
/// error must never fail the mount — every file operation is swallowed so the sidebar
/// shortcut is a nice-to-have, not a mount dependency.
///
/// Both operations are idempotent, matching the contract: re-adding the same path does not
/// duplicate the entry, and removing a path with no matching line is a no-op.
/// </summary>
public sealed class GtkBookmark : ISidebarBookmark
{
    /// <summary>The <c>file://</c> URI scheme prefix every GTK bookmark line carries.</summary>
    private const string FileScheme = "file://";

    private readonly string _bookmarksPath;

    /// <summary>Edits the standard per-user GTK bookmarks file under the config home.</summary>
    public GtkBookmark() : this(ResolveBookmarksPath())
    {
    }

    /// <summary>
    /// Test seam: points the bookmark editing at an explicit file so the add/remove logic
    /// can be exercised against a temporary path without touching the real config.
    /// </summary>
    internal GtkBookmark(string bookmarksPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarksPath);
        _bookmarksPath = bookmarksPath;
    }

    /// <inheritdoc />
    public async Task AddAsync(string path, string label, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var uri = ToFileUri(path);
            var line = string.IsNullOrWhiteSpace(label) ? uri : $"{uri} {label}";

            var lines = (await ReadLinesAsync(ct).ConfigureAwait(false)).ToList();

            // Idempotent: if a bookmark already points at this path, leave the file as-is
            // so re-adding never creates a duplicate entry.
            if (lines.Any(existing => PointsAt(existing, uri)))
                return;

            lines.Add(line);

            await WriteLinesAsync(lines, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort (Req 1.5): the sidebar shortcut is a convenience. A missing
            // config dir or any IO error must never fail the mount.
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            // Nothing to remove when the file was never created.
            if (!File.Exists(_bookmarksPath))
                return;

            var uri = ToFileUri(path);
            var lines = await ReadLinesAsync(ct).ConfigureAwait(false);
            var kept = lines.Where(existing => !PointsAt(existing, uri)).ToList();

            // No matching bookmark: leave the file untouched (no-op).
            if (kept.Count == lines.Count)
                return;

            await WriteLinesAsync(kept, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort (Req 4.1): removing the shortcut must never fail the unmount.
        }
    }

    /// <summary>
    /// Reads the bookmarks file into lines, returning an empty sequence when the file does
    /// not exist yet. Blank lines are preserved so an existing file's layout is kept.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadLinesAsync(CancellationToken ct)
    {
        if (!File.Exists(_bookmarksPath))
            return [];

        var text = await File.ReadAllLinesAsync(_bookmarksPath, ct).ConfigureAwait(false);
        return text;
    }

    /// <summary>
    /// Writes the bookmark lines back, creating <c>~/.config/gtk-3.0</c> if it is missing.
    /// GTK expects a trailing newline, which <see cref="File.WriteAllLinesAsync(string, IEnumerable{string}, CancellationToken)"/> provides.
    /// </summary>
    private async Task WriteLinesAsync(IReadOnlyList<string> lines, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_bookmarksPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(_bookmarksPath, lines, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether an existing bookmark <paramref name="line"/> points at <paramref name="uri"/>.
    /// A GTK line is <c>&lt;uri&gt;[ &lt;label&gt;]</c>; only the URI (the part before the
    /// first space) identifies the bookmark, so the label is ignored on comparison.
    /// </summary>
    private static bool PointsAt(string line, string uri)
    {
        var trimmed = line.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        var lineUri = spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];
        return string.Equals(lineUri, uri, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the <c>file://</c> URI for a local mount path the way GTK stores it.
    /// </summary>
    private static string ToFileUri(string path)
    {
        var full = Path.GetFullPath(path);
        return new UriBuilder(FileScheme + full).Uri.AbsoluteUri;
    }

    /// <summary>
    /// Resolves the standard GTK bookmarks path: <c>$XDG_CONFIG_HOME/gtk-3.0/bookmarks</c>
    /// when that is set, otherwise <c>~/.config/gtk-3.0/bookmarks</c>.
    /// </summary>
    private static string ResolveBookmarksPath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configHome = Path.Combine(home, ".config");
        }

        return Path.Combine(configHome, "gtk-3.0", "bookmarks");
    }
}
