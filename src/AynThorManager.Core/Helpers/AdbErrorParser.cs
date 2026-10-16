using AynThorManager.Core.Models;

namespace AynThorManager.Core.Helpers;

public static class AdbErrorParser
{
    public static Error? TryParseError(string standardOutput, string standardError, string? contextPath = null)
    {
        var combined = $"{standardError} {standardOutput}".ToLowerInvariant();

        switch (combined)
        {
            case var c when c.Contains("no such file or directory"):
                return new Error("PATH_NOT_FOUND", contextPath is not null
                    ? $"O caminho não foi encontrado: {contextPath}"
                    : "O caminho não foi encontrado.");

            case var c when c.Contains("permission denied") || c.Contains("read-only file system"):
                return new Error("PERMISSION_DENIED", contextPath is not null
                    ? $"Permissão negada: {contextPath}"
                    : "Permissão negada.");

            case var c when c.Contains("file exists") || c.Contains("already exists") || c.Contains("cannot move"):
                return new Error("NAME_ALREADY_EXISTS", "Já existe um item com esse nome no diretório.");

            default:
                return null;
        }
    }
}
