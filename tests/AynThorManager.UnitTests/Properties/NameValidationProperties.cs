using System.Text;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using AynThorManager.Core.Validators;

namespace AynThorManager.UnitTests.Properties;

/// <summary>
/// Property 5: Validação de nome de arquivo/diretório
/// For any string, the validator SHALL reject if:
/// (a) contains invalid chars (/ \ : * ? " &lt; &gt; | or control chars U+0000–U+001F),
/// (b) is empty/whitespace-only,
/// (c) is "." or "..",
/// (d) exceeds 255 bytes UTF-8.
/// Otherwise SHALL accept.
///
/// Validates: Requirements 4.2, 4.3, 4.4, 5.2, 5.3, 5.4
/// </summary>
public sealed class NameValidationProperties
{
    /// <summary>
    /// Property: Valid names (no invalid chars, not empty, not reserved, within byte limit)
    /// are accepted by the validator.
    /// 
    /// **Validates: Requirements 4.2, 4.3, 4.4, 5.2, 5.3, 5.4**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = [typeof(NameValidationArbitraries)])]
    public void ValidNames_AreAccepted(ValidName validName)
    {
        var result = FileNameValidator.Validate(validName.Value);

        result.IsSuccess.Should().BeTrue(
            because: $"the name \"{validName.Value}\" contains no invalid chars, " +
                     $"is not empty/reserved, and is within 255 UTF-8 bytes");
    }

    /// <summary>
    /// Property: Names containing at least one invalid character are rejected.
    /// 
    /// **Validates: Requirements 4.2, 5.2**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = [typeof(NameValidationArbitraries)])]
    public void NamesWithInvalidChars_AreRejected(NameWithInvalidChar invalidName)
    {
        var result = FileNameValidator.Validate(invalidName.Value);

        result.IsSuccess.Should().BeFalse(
            because: $"the name \"{EscapeForDisplay(invalidName.Value)}\" contains invalid characters");
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("INVALID_NAME");
    }

    /// <summary>
    /// Property: Names exceeding 255 UTF-8 bytes are rejected with NAME_TOO_LONG.
    /// 
    /// **Validates: Requirements 4.4, 5.3**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = [typeof(NameValidationArbitraries)])]
    public void NamesExceeding255Bytes_AreRejected(NameExceeding255Bytes longName)
    {
        var result = FileNameValidator.Validate(longName.Value);

        result.IsSuccess.Should().BeFalse(
            because: $"the name has {Encoding.UTF8.GetByteCount(longName.Value)} UTF-8 bytes, exceeding the 255-byte limit");
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("NAME_TOO_LONG");
    }

    /// <summary>
    /// Property: Reserved names ("." and "..") are rejected.
    /// 
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public void ReservedNames_AreRejected()
    {
        var reservedNames = new[] { ".", ".." };

        foreach (var reserved in reservedNames)
        {
            var result = FileNameValidator.Validate(reserved);

            result.IsSuccess.Should().BeFalse(
                because: $"the name \"{reserved}\" is reserved");
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("INVALID_NAME");
        }
    }

    private static string EscapeForDisplay(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c < ' ')
                sb.Append($"\\u{(int)c:X4}");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Wrapper types for FsCheck arbitrary registration.
/// </summary>
public sealed record ValidName(string Value);
public sealed record NameWithInvalidChar(string Value);
public sealed record NameExceeding255Bytes(string Value);

/// <summary>
/// FsCheck Arbitrary provider for name validation property tests.
/// </summary>
public sealed class NameValidationArbitraries
{
    private static readonly char[] InvalidChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    public static Arbitrary<ValidName> ValidNameArbitrary()
    {
        var safeChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 _-()+[]{}@#$%&!~`'^,;".ToCharArray();

        var gen = from length in Gen.Choose(1, 80)
                  from chars in Gen.ArrayOf(length, Gen.Elements(safeChars))
                  let name = new string(chars)
                  where !string.IsNullOrWhiteSpace(name)
                  where name != "." && name != ".."
                  where Encoding.UTF8.GetByteCount(name) <= 255
                  select new ValidName(name);

        return Arb.From(gen);
    }

    public static Arbitrary<NameWithInvalidChar> NameWithInvalidCharArbitrary()
    {
        var allInvalidChars = InvalidChars
            .Concat(Enumerable.Range(0, 32).Select(i => (char)i))
            .ToArray();

        var validChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        var gen = from prefixLen in Gen.Choose(0, 10)
                  from prefix in Gen.ArrayOf(prefixLen, Gen.Elements(validChars))
                  from invalidChar in Gen.Elements(allInvalidChars)
                  from suffixLen in Gen.Choose(0, 10)
                  from suffix in Gen.ArrayOf(suffixLen, Gen.Elements(validChars))
                  select new NameWithInvalidChar(new string(prefix) + invalidChar + new string(suffix));

        return Arb.From(gen);
    }

    public static Arbitrary<NameExceeding255Bytes> NameExceeding255BytesArbitrary()
    {
        var gen = from extraBytes in Gen.Choose(1, 100)
                  let totalChars = 256 + extraBytes
                  from chars in Gen.ArrayOf(totalChars, Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray()))
                  select new NameExceeding255Bytes(new string(chars));

        return Arb.From(gen);
    }
}
