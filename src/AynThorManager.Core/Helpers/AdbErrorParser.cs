using AynThorManager.Core.Models;

namespace AynThorManager.Core.Helpers;

public static class AdbErrorParser
{
    public static Error? TryParseError(string standardOutput, string standardError, string? contextPath = null)
    {
        var combined = $"{standardError} {standardOutput}".ToLowerInvariant();

        if (combined.Contains("no such file or directory"))
        {
            var message = contextPath is not null
                ? $"O caminho nÃ£o foi encontrado: {contextPath}"
                : "O caminho nÃ£o foi encontrado.";
            return new Error("PATH_NOT_FOUND", message);
        }

        if (combined.Contains("permission denied") || combined.Contains("read-only file system"))
        {
            var message = contextPath is not null
                ? $"PermissÃ£o negada: {contextPath}"
                : "PermissÃ£o negada.";
            return new Error("PERMISSION_DENIED", message);
        }

        if (combined.Contains("file exists") || combined.Contains("already exists") || combined.Contains("cannot move"))
        {
            return new Error("NAME_ALREADY_EXISTS", "JÃ¡ existe um item com esse nome no diretÃ³rio.");
        }

        return null;
    }
}
