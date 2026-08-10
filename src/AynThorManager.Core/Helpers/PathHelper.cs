namespace AynThorManager.Core.Helpers;

/// <summary>
/// Utility methods for device path manipulation.
/// Centralizes path combination logic to avoid duplication across services.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Combines a parent path and a name into a full path, ensuring a single separator.
    /// </summary>
    public static string Combine(string parentPath, string name) =>
        parentPath.EndsWith('/')
            ? $"{parentPath}{name}"
            : $"{parentPath}/{name}";

    /// <summary>
    /// Extracts the parent directory from a full path.
    /// </summary>
    public static string GetParentPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash > 0 ? trimmed[..lastSlash] : "/";
    }
}
