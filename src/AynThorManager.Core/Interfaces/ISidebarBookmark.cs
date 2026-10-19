namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Manages the native file manager's sidebar bookmarks so the Thor mount point shows up
/// as a friendly shortcut (e.g. "Ayn Thor"). Implemented in the Infrastructure layer
/// (e.g. <c>GtkBookmark</c> editing <c>~/.config/gtk-3.0/bookmarks</c>); the Services
/// layer adds the bookmark after mounting and removes it on unmount. Best-effort: v1
/// targets GTK/Nautilus and never fails the mount when the sidebar cannot be updated
/// (Req 1.5, 4.1).
/// </summary>
public interface ISidebarBookmark
{
    /// <summary>
    /// Adds a sidebar bookmark pointing at the local <paramref name="path"/> (the mount
    /// point) with the display name <paramref name="label"/> (e.g. "Ayn Thor"). Idempotent:
    /// re-adding the same path does not create a duplicate entry. Best-effort — does not
    /// throw when the sidebar cannot be updated.
    /// </summary>
    Task AddAsync(string path, string label, CancellationToken ct = default);

    /// <summary>
    /// Removes the sidebar bookmark pointing at the local <paramref name="path"/>. Safe to
    /// call when no matching bookmark exists (no-op). Best-effort — does not throw when the
    /// sidebar cannot be updated.
    /// </summary>
    Task RemoveAsync(string path, CancellationToken ct = default);
}
