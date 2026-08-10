using AynThorManager.Core.Validators;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;

namespace AynThorManager.UnitTests.Properties;

/// <summary>
/// Property 6: Validação de comprimento de caminho completo
/// 
/// For any combination of parentPath and name, if the total length of
/// (parentPath + "/" + name) exceeds 4096 characters, the validator SHALL reject.
/// Otherwise SHALL accept (regarding length only).
/// 
/// **Validates: Requirements 4.8**
/// </summary>
public sealed class PathLengthProperties
{
    private const int MaxPathLength = 4096;

    /// <summary>
    /// Generates a safe path character (alphanumeric or '/').
    /// </summary>
    private static Gen<char> SafePathChar =>
        Gen.Elements('a', 'b', 'c', 'd', 'e', 'f', '/', '1', '2', '3');

    /// <summary>
    /// Generates a string of exactly the specified length using safe path characters.
    /// </summary>
    private static Gen<string> StringOfLength(int length) =>
        Gen.ArrayOf(length, SafePathChar).Select(chars => new string(chars));

    /// <summary>
    /// Property: When combined path length (parentPath + "/" + name) is within 4096 chars,
    /// ValidateFullPathLength SHALL accept.
    /// 
    /// **Validates: Requirements 4.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AcceptsPathsWithinLimit()
    {
        // Generate parentPath and name such that their combined length <= 4096
        var gen = from parentLen in Gen.Choose(1, 2000)
                  from nameLen in Gen.Choose(1, Math.Min(500, MaxPathLength - parentLen - 1))
                  from parent in StringOfLength(parentLen)
                  from name in StringOfLength(nameLen)
                  // Ensure the parent doesn't already end with '/' for predictable length calculation
                  let cleanParent = parent.TrimEnd('/') 
                  // Only proceed if combined length is actually within limit
                  where (cleanParent + "/" + name).Length <= MaxPathLength
                  select (parentPath: cleanParent, name);

        return Prop.ForAll(gen.ToArbitrary(), pair =>
        {
            var result = PathValidator.ValidateFullPathLength(pair.parentPath, pair.name);

            result.IsSuccess.Should().BeTrue(
                because: $"combined path '{pair.parentPath}/{pair.name}' has length " +
                         $"{pair.parentPath.Length + 1 + pair.name.Length} which is within the {MaxPathLength} char limit");
        });
    }

    /// <summary>
    /// Property: When combined path length (parentPath + "/" + name) exceeds 4096 chars,
    /// ValidateFullPathLength SHALL reject with "PATH_TOO_LONG" error code.
    /// 
    /// **Validates: Requirements 4.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RejectsPathsExceedingLimit()
    {
        // Generate parentPath and name such that their combined length > 4096
        var gen = from parentLen in Gen.Choose(2000, 4000)
                  from extraLen in Gen.Choose(1, 500)
                  let nameLen = MaxPathLength - parentLen + extraLen
                  where nameLen > 0
                  from parent in StringOfLength(parentLen)
                  from name in StringOfLength(nameLen)
                  let cleanParent = parent.TrimEnd('/')
                  // Verify that the full path actually exceeds the limit
                  where (cleanParent + "/" + name).Length > MaxPathLength
                  select (parentPath: cleanParent, name);

        return Prop.ForAll(gen.ToArbitrary(), pair =>
        {
            var result = PathValidator.ValidateFullPathLength(pair.parentPath, pair.name);

            result.IsSuccess.Should().BeFalse(
                because: $"combined path has length {pair.parentPath.Length + 1 + pair.name.Length} " +
                         $"which exceeds the {MaxPathLength} char limit");

            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("PATH_TOO_LONG");
        });
    }

    /// <summary>
    /// Property: When parentPath already ends with '/', the separator is not doubled,
    /// and the total length is still correctly validated against the 4096 limit.
    /// 
    /// **Validates: Requirements 4.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property HandlesTrailingSlashInParentPath()
    {
        // Generate parent paths that end with '/' and verify length calculation is correct
        var gen = from parentLen in Gen.Choose(1, 2000)
                  from nameLen in Gen.Choose(1, 500)
                  from parentBase in StringOfLength(parentLen)
                  from name in StringOfLength(nameLen)
                  let parentWithSlash = parentBase.TrimEnd('/') + "/"
                  let fullPath = parentWithSlash + name
                  select (parentPath: parentWithSlash, name, expectedLength: fullPath.Length);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var result = PathValidator.ValidateFullPathLength(tuple.parentPath, tuple.name);

            if (tuple.expectedLength <= MaxPathLength)
            {
                result.IsSuccess.Should().BeTrue(
                    because: $"full path length {tuple.expectedLength} is within the {MaxPathLength} limit");
            }
            else
            {
                result.IsSuccess.Should().BeFalse(
                    because: $"full path length {tuple.expectedLength} exceeds the {MaxPathLength} limit");
                result.Error!.Code.Should().Be("PATH_TOO_LONG");
            }
        });
    }
}
