namespace AynThorManager.Core.Validators;

using AynThorManager.Core.Models;

public static class IpAddressValidator
{
    private static readonly Error InvalidFormatError = new(
        Code: "INVALID_IP_FORMAT",
        Message: "O endereÃ§o IP fornecido nÃ£o estÃ¡ em formato IPv4 vÃ¡lido (quatro octetos numÃ©ricos de 0 a 255 separados por ponto).");

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
