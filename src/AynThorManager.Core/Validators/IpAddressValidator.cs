namespace AynThorManager.Core.Validators;

using AynThorManager.Core.Models;

/// <summary>
/// Validates IPv4 address format: exactly 4 numeric octets (0–255) separated by dots.
/// </summary>
public static class IpAddressValidator
{
    private static readonly Error InvalidFormatError = new(
        Code: "INVALID_IP_FORMAT",
        Message: "O endereço IP fornecido não está em formato IPv4 válido (quatro octetos numéricos de 0 a 255 separados por ponto).");

    /// <summary>
    /// Validates that the given string is a valid IPv4 address, optionally with a port (ip:port).
    /// </summary>
    /// <param name="ipAddress">The IP address string to validate (e.g. "192.168.1.100" or "192.168.1.100:38383").</param>
    /// <returns>A successful Result if valid; a failure Result with INVALID_IP_FORMAT error otherwise.</returns>
    public static Result Validate(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return Result.Failure(InvalidFormatError);

        // Separate IP and optional port
        var ip = ipAddress;
        if (ipAddress.Contains(':'))
        {
            var colonIndex = ipAddress.LastIndexOf(':');
            ip = ipAddress[..colonIndex];
            var portStr = ipAddress[(colonIndex + 1)..];
            if (!int.TryParse(portStr, out var port) || port is < 1 or > 65535)
                return Result.Failure(InvalidFormatError);
        }

        var parts = ip.Split('.');

        if (parts.Length != 4)
            return Result.Failure(InvalidFormatError);

        foreach (var part in parts)
        {
            if (!IsValidOctet(part))
                return Result.Failure(InvalidFormatError);
        }

        return Result.Success();
    }

    private static bool IsValidOctet(string part)
    {
        // Must not be empty
        if (part.Length == 0)
            return false;

        // Must not contain whitespace
        foreach (var c in part)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        // No leading zeros (except "0" itself)
        if (part.Length > 1 && part[0] == '0')
            return false;

        // Must be a valid integer 0–255
        if (!int.TryParse(part, out var value))
            return false;

        return value is >= 0 and <= 255;
    }
}
