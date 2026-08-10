using AynThorManager.Core.Models;
using AynThorManager.Core.Validators;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace AynThorManager.UnitTests.Properties;

/// <summary>
/// Property 8: Validação de limites de upload
/// Feature: adb-file-management, Property 8: Validação de limites de upload
///
/// For any batch of files selected for upload, if the file count is greater than 20
/// OR if any individual file exceeds 4 GB (4,294,967,296 bytes), the service SHALL
/// reject the operation before starting any transfer.
///
/// **Validates: Requirements 3.6**
/// </summary>
public sealed class FileLimitsProperties
{
    private const int MaxFiles = 20;
    private const long MaxFileSize = 4L * 1024 * 1024 * 1024; // 4 GB

    /// <summary>
    /// Generates a valid TransferFile with size within the 4 GB limit.
    /// </summary>
    private static Gen<TransferFile> ValidTransferFileGen =>
        from size in Gen.Choose(0, (int)Math.Min(MaxFileSize, int.MaxValue))
        from nameLen in Gen.Choose(1, 20)
        from nameChars in Gen.ArrayOf(nameLen, Gen.Elements('a', 'b', 'c', 'd', 'e', 'f', 'g', '1', '2', '3'))
        let fileName = new string(nameChars) + ".bin"
        select new TransferFile($"C:\\files\\{fileName}", fileName, (long)size);

    /// <summary>
    /// Generates a TransferFile with a valid size using the full long range up to MaxFileSize.
    /// </summary>
    private static Gen<TransferFile> ValidSizeTransferFileGen =>
        from sizeLow in Gen.Choose(0, int.MaxValue)
        from sizeHigh in Gen.Choose(0, 3) // 0-3 GB range in high portion
        let size = (long)sizeHigh * 1024L * 1024L * 1024L + (long)sizeLow % (1024L * 1024L * 1024L)
        where size >= 0 && size <= MaxFileSize
        from nameLen in Gen.Choose(1, 15)
        from nameChars in Gen.ArrayOf(nameLen, Gen.Elements('a', 'b', 'c', 'd', 'e', 'f', 'g'))
        let fileName = new string(nameChars) + ".rom"
        select new TransferFile($"/local/{fileName}", fileName, size);

    /// <summary>
    /// Generates a TransferFile with size exceeding 4 GB.
    /// </summary>
    private static Gen<TransferFile> OversizedTransferFileGen =>
        from extraBytes in Gen.Choose(1, int.MaxValue)
        from nameLen in Gen.Choose(1, 15)
        from nameChars in Gen.ArrayOf(nameLen, Gen.Elements('a', 'b', 'c', 'd', 'e', 'f', 'g'))
        let fileName = new string(nameChars) + ".iso"
        select new TransferFile($"/local/{fileName}", fileName, MaxFileSize + (long)extraBytes);

    /// <summary>
    /// Property: When file count is within limit (1–20) AND all files are within size limit (≤ 4 GB),
    /// the validation SHALL accept the batch.
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValidBatch_WithinLimits_IsAccepted()
    {
        var gen = from count in Gen.Choose(1, MaxFiles)
                  from files in Gen.ListOf(count, ValidSizeTransferFileGen)
                  select files.ToList().AsReadOnly() as IReadOnlyList<TransferFile>;

        return Prop.ForAll(gen.ToArbitrary(), files =>
        {
            var result = TransferValidator.ValidateFileLimits(files);

            result.IsSuccess.Should().BeTrue(
                because: $"a batch of {files.Count} files (all ≤ 4 GB) is within upload limits");
        });
    }

    /// <summary>
    /// Property: When file count exceeds 20, the validation SHALL reject the batch
    /// with FILE_LIMIT_EXCEEDED error, regardless of individual file sizes.
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TooManyFiles_IsRejected()
    {
        var gen = from count in Gen.Choose(MaxFiles + 1, 50)
                  from files in Gen.ListOf(count, ValidSizeTransferFileGen)
                  select files.ToList().AsReadOnly() as IReadOnlyList<TransferFile>;

        return Prop.ForAll(gen.ToArbitrary(), files =>
        {
            var result = TransferValidator.ValidateFileLimits(files);

            result.IsSuccess.Should().BeFalse(
                because: $"a batch of {files.Count} files exceeds the maximum of {MaxFiles}");
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("FILE_LIMIT_EXCEEDED");
        });
    }

    /// <summary>
    /// Property: When any single file exceeds 4 GB, the validation SHALL reject the batch
    /// with FILE_LIMIT_EXCEEDED error, even if file count is within limit.
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OversizedFile_IsRejected()
    {
        var gen = from validCount in Gen.Choose(0, MaxFiles - 1)
                  from validFiles in Gen.ListOf(validCount, ValidSizeTransferFileGen)
                  from oversized in OversizedTransferFileGen
                  from insertPos in Gen.Choose(0, validCount)
                  let allFiles = validFiles.Take(insertPos)
                      .Append(oversized)
                      .Concat(validFiles.Skip(insertPos))
                      .ToList()
                      .AsReadOnly()
                  where allFiles.Count <= MaxFiles // keep within count limit to isolate size violation
                  select allFiles as IReadOnlyList<TransferFile>;

        return Prop.ForAll(gen.ToArbitrary(), files =>
        {
            var result = TransferValidator.ValidateFileLimits(files);

            result.IsSuccess.Should().BeFalse(
                because: "at least one file exceeds the 4 GB size limit");
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("FILE_LIMIT_EXCEEDED");
        });
    }

    /// <summary>
    /// Property: When BOTH violations occur (count > 20 AND at least one file > 4 GB),
    /// the validation SHALL still reject the batch.
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BothLimitsViolated_IsRejected()
    {
        var gen = from count in Gen.Choose(MaxFiles + 1, 40)
                  from validFiles in Gen.ListOf(count - 1, ValidSizeTransferFileGen)
                  from oversized in OversizedTransferFileGen
                  let allFiles = validFiles.Append(oversized).ToList().AsReadOnly()
                  select allFiles as IReadOnlyList<TransferFile>;

        return Prop.ForAll(gen.ToArbitrary(), files =>
        {
            var result = TransferValidator.ValidateFileLimits(files);

            result.IsSuccess.Should().BeFalse(
                because: "both file count and file size limits are exceeded");
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("FILE_LIMIT_EXCEEDED");
        });
    }

    /// <summary>
    /// Property: An empty file list (count = 0) SHALL be accepted since no limits are violated.
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EmptyFileList_IsAccepted()
    {
        return Prop.ForAll(Arb.Default.Unit().Generator.ToArbitrary(), _ =>
        {
            var files = Array.Empty<TransferFile>() as IReadOnlyList<TransferFile>;

            var result = TransferValidator.ValidateFileLimits(files);

            result.IsSuccess.Should().BeTrue(
                because: "an empty file list does not violate any upload limits");
        });
    }

    /// <summary>
    /// Property: A file with exactly 4 GB (4,294,967,296 bytes) SHALL be accepted (boundary).
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FileAtExactly4GB_IsAccepted()
    {
        var gen = from count in Gen.Choose(1, MaxFiles)
                  from nameLen in Gen.Choose(1, 10)
                  from nameChars in Gen.ArrayOf(nameLen, Gen.Elements('a', 'b', 'c', 'd'))
                  let fileName = new string(nameChars) + ".bin"
                  let boundaryFile = new TransferFile($"/local/{fileName}", fileName, MaxFileSize)
                  from otherCount in Gen.Choose(0, count - 1)
                  from otherFiles in Gen.ListOf(otherCount, ValidSizeTransferFileGen)
                  let allFiles = otherFiles.Prepend(boundaryFile).ToList().AsReadOnly()
                  where allFiles.Count <= MaxFiles
                  select allFiles as IReadOnlyList<TransferFile>;

        return Prop.ForAll(gen.ToArbitrary(), files =>
        {
            var result = TransferValidator.ValidateFileLimits(files);

            result.IsSuccess.Should().BeTrue(
                because: "a file with exactly 4 GB (4,294,967,296 bytes) is at the boundary and should be accepted");
        });
    }

    /// <summary>
    /// Property: A file with exactly 4 GB + 1 byte SHALL be rejected.
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FileAtExactly4GBPlusOneByte_IsRejected()
    {
        var gen = from nameLen in Gen.Choose(1, 10)
                  from nameChars in Gen.ArrayOf(nameLen, Gen.Elements('a', 'b', 'c', 'd'))
                  let fileName = new string(nameChars) + ".bin"
                  let oversizedFile = new TransferFile($"/local/{fileName}", fileName, MaxFileSize + 1)
                  select new List<TransferFile> { oversizedFile }.AsReadOnly() as IReadOnlyList<TransferFile>;

        return Prop.ForAll(gen.ToArbitrary(), files =>
        {
            var result = TransferValidator.ValidateFileLimits(files);

            result.IsSuccess.Should().BeFalse(
                because: "a file with 4 GB + 1 byte exceeds the maximum file size");
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("FILE_LIMIT_EXCEEDED");
        });
    }

    /// <summary>
    /// Property: Exactly 20 files (boundary) with all sizes within limit SHALL be accepted.
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExactlyMaxFiles_IsAccepted()
    {
        var gen = Gen.ListOf(MaxFiles, ValidSizeTransferFileGen)
                    .Select(files => files.ToList().AsReadOnly() as IReadOnlyList<TransferFile>);

        return Prop.ForAll(gen.ToArbitrary(), files =>
        {
            files.Count.Should().Be(MaxFiles);

            var result = TransferValidator.ValidateFileLimits(files);

            result.IsSuccess.Should().BeTrue(
                because: $"exactly {MaxFiles} files is at the boundary and should be accepted");
        });
    }

    /// <summary>
    /// Property: Exactly 21 files (one over boundary) SHALL be rejected regardless of sizes.
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExactlyMaxFilesPlusOne_IsRejected()
    {
        var gen = Gen.ListOf(MaxFiles + 1, ValidSizeTransferFileGen)
                    .Select(files => files.ToList().AsReadOnly() as IReadOnlyList<TransferFile>);

        return Prop.ForAll(gen.ToArbitrary(), files =>
        {
            files.Count.Should().Be(MaxFiles + 1);

            var result = TransferValidator.ValidateFileLimits(files);

            result.IsSuccess.Should().BeFalse(
                because: $"{MaxFiles + 1} files exceeds the maximum allowed count of {MaxFiles}");
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("FILE_LIMIT_EXCEEDED");
        });
    }
}
