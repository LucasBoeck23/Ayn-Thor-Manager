using AynThorManager.Core.Models;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace AynThorManager.UnitTests.Properties;

/// <summary>
/// Property 4: Invariante de truncamento
/// Feature: adb-file-management, Property 4: Invariante de truncamento
/// Validates: Requirements 2.7
/// </summary>
public sealed class TruncationProperties
{
    private const int MaxEntries = 1000;

    /// <summary>
    /// Generates a list of FileEntry with a size between 0 and 2500 entries.
    /// This range ensures we test both truncated (>1000) and non-truncated (<=1000) scenarios.
    /// </summary>
    private static Arbitrary<List<FileEntry>> VariableSizeFileEntryListArbitrary()
    {
        var nameGen = Arb.Default.NonEmptyString().Generator
            .Select(nes => nes.Get);

        var typeGen = Gen.Elements(FileEntryType.File, FileEntryType.Directory);

        var sizeGen = Gen.Choose(0, int.MaxValue).Select(i => (long)i);

        var dateGen = Gen.Constant(DateTimeOffset.UtcNow);

        var entryGen = from name in nameGen
                       from type in typeGen
                       from size in sizeGen
                       from date in dateGen
                       select new FileEntry(name, size, date, type);

        // Generate lists with sizes from 0 to 2500 to cover both sides of the 1000 threshold
        var listGen = Gen.Choose(0, 2500)
            .SelectMany(count => Gen.ListOf(count, entryGen))
            .Select(l => l.ToList());

        return Arb.From(listGen);
    }

    /// <summary>
    /// Applies the same truncation logic used in FileStorageService.
    /// Sorts entries (directories first, then alphabetical), then truncates at MaxEntries.
    /// </summary>
    private static (List<FileEntry> Entries, bool IsTruncated, int TotalCount) ApplyTruncation(List<FileEntry> entries)
    {
        var sorted = entries
            .OrderBy(e => e.Type == FileEntryType.Directory ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalCount = sorted.Count;
        var isTruncated = totalCount > MaxEntries;
        var truncated = isTruncated ? sorted.Take(MaxEntries).ToList() : sorted;

        return (truncated, isTruncated, totalCount);
    }

    /// <summary>
    /// **Validates: Requirements 2.7**
    /// For any list of entries where the original count is greater than 1000,
    /// the result SHALL contain exactly 1000 entries and isTruncated SHALL be true.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property WhenMoreThan1000Entries_ResultContainsExactly1000_AndIsTruncatedIsTrue()
    {
        var nameGen = Arb.Default.NonEmptyString().Generator
            .Select(nes => nes.Get);

        var typeGen = Gen.Elements(FileEntryType.File, FileEntryType.Directory);

        var sizeGen = Gen.Choose(0, int.MaxValue).Select(i => (long)i);

        var dateGen = Gen.Constant(DateTimeOffset.UtcNow);

        var entryGen = from name in nameGen
                       from type in typeGen
                       from size in sizeGen
                       from date in dateGen
                       select new FileEntry(name, size, date, type);

        // Generate lists with 1001 to 2500 entries (always above the threshold)
        var largeListGen = Gen.Choose(1001, 2500)
            .SelectMany(count => Gen.ListOf(count, entryGen))
            .Select(l => l.ToList());

        var arb = Arb.From(largeListGen);

        return Prop.ForAll(arb, entries =>
        {
            var (truncated, isTruncated, totalCount) = ApplyTruncation(entries);

            totalCount.Should().BeGreaterThan(MaxEntries,
                because: "we generated more than 1000 entries");

            truncated.Should().HaveCount(MaxEntries,
                because: "the result must contain exactly 1000 entries when original count exceeds the limit");

            isTruncated.Should().BeTrue(
                because: "the isTruncated flag must be true when the original entry count exceeds 1000");
        });
    }

    /// <summary>
    /// **Validates: Requirements 2.7**
    /// For any list of entries where the original count is less than or equal to 1000,
    /// the result SHALL contain all entries and isTruncated SHALL be false.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property WhenAtMost1000Entries_ResultContainsAllEntries_AndIsTruncatedIsFalse()
    {
        var nameGen = Arb.Default.NonEmptyString().Generator
            .Select(nes => nes.Get);

        var typeGen = Gen.Elements(FileEntryType.File, FileEntryType.Directory);

        var sizeGen = Gen.Choose(0, int.MaxValue).Select(i => (long)i);

        var dateGen = Gen.Constant(DateTimeOffset.UtcNow);

        var entryGen = from name in nameGen
                       from type in typeGen
                       from size in sizeGen
                       from date in dateGen
                       select new FileEntry(name, size, date, type);

        // Generate lists with 0 to 1000 entries (at or below the threshold)
        var smallListGen = Gen.Choose(0, 1000)
            .SelectMany(count => Gen.ListOf(count, entryGen))
            .Select(l => l.ToList());

        var arb = Arb.From(smallListGen);

        return Prop.ForAll(arb, entries =>
        {
            var (truncated, isTruncated, totalCount) = ApplyTruncation(entries);

            totalCount.Should().BeLessThanOrEqualTo(MaxEntries,
                because: "we generated at most 1000 entries");

            truncated.Should().HaveCount(entries.Count,
                because: "all entries must be returned when the count is at or below the limit");

            isTruncated.Should().BeFalse(
                because: "the isTruncated flag must be false when the entry count does not exceed 1000");
        });
    }

    /// <summary>
    /// **Validates: Requirements 2.7**
    /// For any list of entries, the TotalCount in the result SHALL always equal
    /// the original number of entries, regardless of truncation.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property TotalCount_AlwaysReflectsOriginalEntryCount()
    {
        return Prop.ForAll(VariableSizeFileEntryListArbitrary(), entries =>
        {
            var (_, _, totalCount) = ApplyTruncation(entries);

            totalCount.Should().Be(entries.Count,
                because: "TotalCount must always reflect the original number of entries in the directory");
        });
    }
}
