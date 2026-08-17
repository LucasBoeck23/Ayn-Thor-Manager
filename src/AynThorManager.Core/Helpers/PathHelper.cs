namespace AynThorManager.Core.Helpers;

public static class PathHelper
{
    public static string Combine(string parentPath, string name) =>
        parentPath.EndsWith('/')
            ? $"{parentPath}{name}"
            : $"{parentPath}/{name}";

    public static string GetParentPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash > 0 ? trimmed[..lastSlash] : "/";
    }
}
