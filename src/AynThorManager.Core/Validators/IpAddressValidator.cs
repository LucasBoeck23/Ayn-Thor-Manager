namespace AynThorManager.Core.Validators;

using AynThorManager.Core.Models;

public static class IpAddressValidator
{
    private static readonly Error InvalidFormatError = new(
        Code: "INVALID_IP_FORMAT",
        Message: "Endereço inválido. Use o formato IP ou IP:porta, por exemplo 192.168.1.17:43321.");

    public static Result Validate(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return Result.Failure(InvalidFormatError);

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
        if (part.Length == 0)
            return false;

        foreach (var c in part)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        if (part.Length > 1 && part[0] == '0')
            return false;

        if (!int.TryParse(part, out var value))
            return false;

        return value is >= 0 and <= 255;
    }
}
