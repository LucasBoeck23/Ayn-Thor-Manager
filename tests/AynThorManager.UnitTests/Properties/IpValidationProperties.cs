using AynThorManager.Core.Validators;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace AynThorManager.UnitTests.Properties;

/// <summary>
/// Property 2: Validação de formato IPv4
/// Feature: adb-file-management, Property 2: Validação de formato IPv4
/// Validates: Requirements 1.6
/// </summary>
public sealed class IpValidationProperties
{
    /// <summary>
    /// Generates a valid IPv4 address: 4 random bytes formatted as "x.x.x.x" (0–255 each, no leading zeros).
    /// </summary>
    private static Arbitrary<string> ValidIpArbitrary()
    {
        var gen = from b0 in Gen.Choose(0, 255)
                  from b1 in Gen.Choose(0, 255)
                  from b2 in Gen.Choose(0, 255)
                  from b3 in Gen.Choose(0, 255)
                  select $"{b0}.{b1}.{b2}.{b3}";

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates arbitrary non-null strings that do NOT match the valid IPv4 pattern.
    /// Filters out strings that happen to be valid IPs.
    /// </summary>
    private static Arbitrary<string> InvalidIpArbitrary()
    {
        var gen = Arb.Default.NonNull<string>().Generator
            .Select(nns => nns.Get)
            .Where(s => !IsValidIpv4(s));

        return Arb.From(gen);
    }

    /// <summary>
    /// Reference implementation: checks if a string is a valid IPv4 address
    /// (exactly 4 numeric octets 0–255 separated by dots, no leading zeros except "0" itself).
    /// </summary>
    private static bool IsValidIpv4(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var parts = s.Split('.');
        if (parts.Length != 4)
            return false;

        foreach (var part in parts)
        {
            if (part.Length == 0)
                return false;

            // All characters must be ASCII digits
            foreach (var c in part)
            {
                if (c < '0' || c > '9')
                    return false;
            }

            // No leading zeros (except "0" itself)
            if (part.Length > 1 && part[0] == '0')
                return false;

            if (!int.TryParse(part, out var value))
                return false;

            if (value < 0 || value > 255)
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 1.6**
    /// For any valid IPv4 address (4 octets 0–255), the validator SHALL accept the input.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property ValidIpv4Addresses_AreAlwaysAccepted()
    {
        return Prop.ForAll(ValidIpArbitrary(), ip =>
        {
            var result = IpAddressValidator.Validate(ip);
            result.IsSuccess.Should().BeTrue(
                because: $"'{ip}' is a valid IPv4 address with 4 octets in range 0–255");
        });
    }

    /// <summary>
    /// **Validates: Requirements 1.6**
    /// For any string that does NOT match the valid IPv4 format, the validator SHALL reject the input.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property InvalidStrings_AreAlwaysRejected()
    {
        return Prop.ForAll(InvalidIpArbitrary(), input =>
        {
            var result = IpAddressValidator.Validate(input);
            result.IsSuccess.Should().BeFalse(
                because: $"'{input}' is not a valid IPv4 address");
            result.Error!.Code.Should().Be("INVALID_IP_FORMAT");
        });
    }
}
