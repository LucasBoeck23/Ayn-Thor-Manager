using AynThorManager.Core.Validators;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;

namespace AynThorManager.UnitTests.Properties;

/// <summary>
/// Property 7: Verificação de espaço pré-transferência
/// 
/// For any set of files selected for upload and a device available space value,
/// if the sum of all file sizes exceeds the available space, the service SHALL reject
/// the operation before starting any transfer.
/// 
/// **Validates: Requirements 3.4**
/// </summary>
public sealed class SpaceCheckProperties
{
    /// <summary>
    /// Generates positive longs suitable for file sizes and available space.
    /// Range: 1 byte to 100 GB (covers realistic file transfer scenarios).
    /// </summary>
    private static Gen<long> PositiveLongGen =>
        Gen.Choose(1, int.MaxValue)
            .Select(v => (long)v)
            .Or(Gen.Choose(1, 100).Select(gb => gb * 1024L * 1024L * 1024L));

    /// <summary>
    /// Property: When total file size exceeds available space, the space check SHALL reject
    /// with INSUFFICIENT_SPACE error code.
    /// 
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RejectsWhenInsufficientSpace()
    {
        var gen = from totalFileSize in PositiveLongGen
                  from deficit in Gen.Choose(1, int.MaxValue).Select(d => (long)d)
                  let availableSpace = totalFileSize - deficit
                  where availableSpace >= 0 && totalFileSize > availableSpace
                  select (totalFileSize, availableSpace);

        return Prop.ForAll(gen.ToArbitrary(), pair =>
        {
            var result = TransferValidator.ValidateAvailableSpace(pair.totalFileSize, pair.availableSpace);

            result.IsSuccess.Should().BeFalse(
                because: $"total file size ({pair.totalFileSize} bytes) exceeds " +
                         $"available space ({pair.availableSpace} bytes)");

            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("INSUFFICIENT_SPACE");
            result.Error.Details.Should().NotBeNull();
            result.Error.Details!["requiredBytes"].Should().Be(pair.totalFileSize);
            result.Error.Details["availableBytes"].Should().Be(pair.availableSpace);
        });
    }

    /// <summary>
    /// Property: When total file size is less than or equal to available space,
    /// the space check SHALL accept (pass).
    /// 
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AcceptsWhenSufficientSpace()
    {
        var gen = from totalFileSize in PositiveLongGen
                  from extra in Gen.Choose(0, int.MaxValue).Select(e => (long)e)
                  let availableSpace = totalFileSize + extra
                  where availableSpace >= totalFileSize // guard against overflow
                  select (totalFileSize, availableSpace);

        return Prop.ForAll(gen.ToArbitrary(), pair =>
        {
            var result = TransferValidator.ValidateAvailableSpace(pair.totalFileSize, pair.availableSpace);

            result.IsSuccess.Should().BeTrue(
                because: $"total file size ({pair.totalFileSize} bytes) does not exceed " +
                         $"available space ({pair.availableSpace} bytes)");
        });
    }

    /// <summary>
    /// Property: When total file size equals available space exactly,
    /// the space check SHALL accept (boundary condition — space is sufficient).
    /// 
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AcceptsWhenSpaceExactlyEquals()
    {
        var gen = PositiveLongGen.Select(size => (totalFileSize: size, availableSpace: size));

        return Prop.ForAll(gen.ToArbitrary(), pair =>
        {
            var result = TransferValidator.ValidateAvailableSpace(pair.totalFileSize, pair.availableSpace);

            result.IsSuccess.Should().BeTrue(
                because: $"total file size ({pair.totalFileSize} bytes) equals " +
                         $"available space ({pair.availableSpace} bytes) — space is sufficient");
        });
    }
}
