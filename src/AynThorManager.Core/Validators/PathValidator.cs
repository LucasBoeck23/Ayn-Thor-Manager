using AynThorManager.Core.Helpers;
using AynThorManager.Core.Models;

namespace AynThorManager.Core.Validators;

public static class PathValidator
{
    private const int MaxPathLength = 4096;

    private static readonly string[] AllowedPrefixes =
    [
        "/sdcard/",
        "/storage/emulated/0/",
        "/storage/"  // microSD: /storage/{uuid}/
    ];

    public static Result Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result.Failure(new Error("PATH_NOT_ALLOWED", "Path cannot be empty."));

        if (path.Length > MaxPathLength)
            return Result.Failure(new Error("PATH_TOO_LONG", $"Path exceeds the maximum length of {MaxPathLength} characters."));

        if (path.Contains("../") || path.Contains("..\\"))
            return Result.Failure(new Error("PATH_NOT_ALLOWED", "Path contains directory traversal sequences."));

        if (!AllowedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
            return Result.Failure(new Error("PATH_NOT_ALLOWED", "Path does not start with an allowed storage prefix."));

        return Result.Success();
    }

    public static Result ValidateFullPathLength(string parentPath, string name)
    {
        var fullPath = PathHelper.Combine(parentPath, name);

        if (fullPath.Length > MaxPathLength)
            return Result.Failure(new Error("PATH_TOO_LONG", $"Full path exceeds the maximum length of {MaxPathLength} characters."));

        return Result.Success();
    }
}
