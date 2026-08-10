using AynThorManager.Core.Validators;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace AynThorManager.UnitTests.Properties;

/// <summary>
/// Property 9: Prevenção de path traversal
/// Validates: Requirements 6.3
/// </summary>
public sealed class PathSafetyProperties
{
    private static readonly string[] AllowedPrefixes =
    [
        "/sdcard/",
        "/storage/emulated/0/",
        "/storage/"
    ];

    /// <summary>
    /// Generates a valid path segment (no "../", no backslashes, printable ASCII).
    /// </summary>
    private static Gen<string> SafeSegmentGen()
    {
        return Gen.Elements(
            "Documents", "ROMs", "SNES", "GBA", "Music", "Photos",
            "game.zip", "rom.bin", "save.dat", "config.txt", "data",
            "folder1", "folder2", "my-files", "test_dir", "archive"
        );
    }

    /// <summary>
    /// Generates a safe path: valid prefix + 1-5 safe segments joined by "/" with total length ≤ 4096.
    /// </summary>
    private static Arbitrary<string> SafePathArbitrary()
    {
        var gen = from prefix in Gen.Elements(AllowedPrefixes)
                  from segmentCount in Gen.Choose(1, 5)
                  from segments in Gen.ListOf(segmentCount, SafeSegmentGen())
                  let path = prefix + string.Join("/", segments)
                  where path.Length <= 4096
                  select path;

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates a path that contains "../" somewhere.
    /// </summary>
    private static Arbitrary<string> TraversalPathArbitrary()
    {
        var gen = from prefix in Gen.Elements(AllowedPrefixes)
                  from beforeSegments in Gen.Choose(0, 3)
                  from before in Gen.ListOf(beforeSegments, SafeSegmentGen())
                  from afterSegments in Gen.Choose(0, 3)
                  from after in Gen.ListOf(afterSegments, SafeSegmentGen())
                  let beforePart = before.Any() ? string.Join("/", before) + "/" : ""
                  let afterPart = after.Any() ? "/" + string.Join("/", after) : ""
                  let path = prefix + beforePart + "../" + afterPart
                  select path;

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates a path without a valid prefix (does not start with any allowed prefix).
    /// </summary>
    private static Arbitrary<string> InvalidPrefixPathArbitrary()
    {
        var invalidPrefixes = new[]
        {
            "/home/", "/tmp/", "/etc/", "/root/", "/data/",
            "/system/", "/proc/", "/mnt/", "sdcard/",
            "C:\\Users\\", "/var/", "/usr/", "/opt/"
        };

        var gen = from prefix in Gen.Elements(invalidPrefixes)
                  from segmentCount in Gen.Choose(1, 4)
                  from segments in Gen.ListOf(segmentCount, SafeSegmentGen())
                  let path = prefix + string.Join("/", segments)
                  where !AllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal))
                  select path;

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates a path with valid prefix but also containing "../".
    /// </summary>
    private static Arbitrary<string> ValidPrefixWithTraversalArbitrary()
    {
        var gen = from prefix in Gen.Elements(AllowedPrefixes)
                  from segment in SafeSegmentGen()
                  from position in Gen.Elements("before", "middle", "end")
                  let path = position switch
                  {
                      "before" => prefix + "../" + segment,
                      "middle" => prefix + segment + "/../" + segment,
                      "end" => prefix + segment + "/../",
                      _ => prefix + "../" + segment
                  }
                  select path;

        return Arb.From(gen);
    }

    /// <summary>
    /// **Validates: Requirements 6.3**
    /// Safe paths (valid prefix + no "../" + length ≤ 4096) SHALL be accepted.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SafePaths_WithValidPrefixAndNoTraversal_AreAccepted()
    {
        return Prop.ForAll(SafePathArbitrary(), path =>
        {
            var result = PathValidator.Validate(path);
            result.IsSuccess.Should().BeTrue(
                because: $"path '{path}' has a valid prefix and no traversal sequences");
        });
    }

    /// <summary>
    /// **Validates: Requirements 6.3**
    /// Paths containing "../" anywhere SHALL be rejected.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PathsWithTraversal_AreAlwaysRejected()
    {
        return Prop.ForAll(TraversalPathArbitrary(), path =>
        {
            var result = PathValidator.Validate(path);
            result.IsSuccess.Should().BeFalse(
                because: $"path '{path}' contains '../' traversal sequence");
            result.Error!.Code.Should().Be("PATH_NOT_ALLOWED");
        });
    }

    /// <summary>
    /// **Validates: Requirements 6.3**
    /// Paths without a valid prefix SHALL be rejected.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PathsWithoutValidPrefix_AreAlwaysRejected()
    {
        return Prop.ForAll(InvalidPrefixPathArbitrary(), path =>
        {
            var result = PathValidator.Validate(path);
            result.IsSuccess.Should().BeFalse(
                because: $"path '{path}' does not start with an allowed prefix");
            result.Error!.Code.Should().Be("PATH_NOT_ALLOWED");
        });
    }

    /// <summary>
    /// **Validates: Requirements 6.3**
    /// Paths with valid prefix but containing "../" SHALL still be rejected.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PathsWithValidPrefixButTraversal_AreRejected()
    {
        return Prop.ForAll(ValidPrefixWithTraversalArbitrary(), path =>
        {
            var result = PathValidator.Validate(path);
            result.IsSuccess.Should().BeFalse(
                because: $"path '{path}' has valid prefix but contains '../' traversal");
            result.Error!.Code.Should().Be("PATH_NOT_ALLOWED");
        });
    }
}
