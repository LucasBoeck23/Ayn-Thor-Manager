using AynThorManager.Core.Models;

namespace AynThorManager.Core.Helpers;

/// <summary>
/// Parses common ADB command output errors into structured Error objects.
/// Eliminates repetitive string matching across service methods.
/// </summary>
public static class AdbErrorParser
{
    /// <summary>
    /// Attempts to parse known ADB error patterns from combined command output.
    /// Returns null if no known error pattern is detected.
    /// </summary>
    /// <param name="standardOutput">The command's stdout.</param>
    /// <param name="standardError">The command's stderr.</param>
    /// <param name="contextPath">Optional path for error messages.</param>
    public static Error? TryParseError(string standardOutput, string standardError, string? contextPath = null)
    {
        var combined = $"{standardError} {standardOutput}".ToLowerInvariant();

        if (combined.Contains("no such file or directory"))
        {
            var message = contextPath is not null
                ? $"O caminho não foi encontrado: {contextPath}"
                : "O caminho não foi encontrado.";
            return new Error("PATH_NOT_FOUND", message);
        }

        if (combined.Contains("permission denied") || combined.Contains("read-only file system"))
        {
            var message = contextPath is not null
                ? $"Permissão negada: {contextPath}"
                : "Permissão negada.";
            return new Error("PERMISSION_DENIED", message);
        }

        if (combined.Contains("file exists") || combined.Contains("already exists") || combined.Contains("cannot move"))
        {
            return new Error("NAME_ALREADY_EXISTS", "Já existe um item com esse nome no diretório.");
        }

        return null;
    }
}
