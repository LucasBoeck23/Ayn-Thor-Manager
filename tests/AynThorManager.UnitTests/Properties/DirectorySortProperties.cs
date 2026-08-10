using AynThorManager.Core.Models;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace AynThorManager.UnitTests.Properties;

/// <summary>
/// Property 3: Invariante de ordenação na listagem de diretórios
/// Feature: adb-file-management, Property 3: Invariante de ordenação na listagem de diretórios
/// Validates: Requirements 2.1
/// </summary>
public sealed class DirectorySortProperties
{
    /// <summary>
    /// Generates a random FileEntry with a random name (non-empty, no null) and random type.
    /// </summary>
    private static Arbitrary<List<FileEntry>> FileEntryListArbitrary()
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

        var listGen = Gen.ListOf(entryGen).Select(l => l.ToList());

        return Arb.From(listGen);
    }

    /// <summary>
    /// Applies the same sort logic used in FileStorageService:
    /// directories first, then files, alphabetical (case-insensitive) within each group.
    /// </summary>
    private static List<FileEntry> ApplySort(List<FileEntry> entries) =>
        entries
            .OrderBy(e => e.Type == FileEntryType.Directory ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// **Validates: Requirements 2.1**
    /// For any list of FileEntry, after sorting, all directories SHALL appear before all files.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property AllDirectories_AppearBeforeAllFiles()
    {
        return Prop.ForAll(FileEntryListArbitrary(), entries =>
        {
            var sorted = ApplySort(entries);

            // Find the index of the last directory and the first file
            var lastDirectoryIndex = -1;
            var firstFileIndex = sorted.Count; // default to "past end" if no files

            for (var i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Type == FileEntryType.Directory)
                    lastDirectoryIndex = i;
                else if (firstFileIndex == sorted.Count)
                    firstFileIndex = i;
            }

            // If both directories and files exist, last directory must come before first file
            if (lastDirectoryIndex >= 0 && firstFileIndex < sorted.Count)
            {
                lastDirectoryIndex.Should().BeLessThan(firstFileIndex,
                    because: "all directories must appear before all files in the sorted list");
            }
        });
    }

    /// <summary>
    /// **Validates: Requirements 2.1**
    /// For any list of FileEntry, after sorting, names within the directory group
    /// SHALL be in alphabetical ascending order (case-insensitive).
    /// </summary>
    [Property(MaxTest = 200)]
    public Property DirectoryNames_AreSortedAlphabetically()
    {
        return Prop.ForAll(FileEntryListArbitrary(), entries =>
        {
            var sorted = ApplySort(entries);

            var directoryNames = sorted
                .Where(e => e.Type == FileEntryType.Directory)
                .Select(e => e.Name)
                .ToList();

            for (var i = 1; i < directoryNames.Count; i++)
            {
                var comparison = StringComparer.OrdinalIgnoreCase.Compare(
                    directoryNames[i - 1], directoryNames[i]);

                comparison.Should().BeLessThanOrEqualTo(0,
                    because: $"directory '{directoryNames[i - 1]}' should come before or equal to '{directoryNames[i]}' in case-insensitive alphabetical order");
            }
        });
    }

    /// <summary>
    /// **Validates: Requirements 2.1**
    /// For any list of FileEntry, after sorting, names within the file group
    /// SHALL be in alphabetical ascending order (case-insensitive).
    /// </summary>
    [Property(MaxTest = 200)]
    public Property FileNames_AreSortedAlphabetically()
    {
        return Prop.ForAll(FileEntryListArbitrary(), entries =>
        {
            var sorted = ApplySort(entries);

            var fileNames = sorted
                .Where(e => e.Type == FileEntryType.File)
                .Select(e => e.Name)
                .ToList();

            for (var i = 1; i < fileNames.Count; i++)
            {
                var comparison = StringComparer.OrdinalIgnoreCase.Compare(
                    fileNames[i - 1], fileNames[i]);

                comparison.Should().BeLessThanOrEqualTo(0,
                    because: $"file '{fileNames[i - 1]}' should come before or equal to '{fileNames[i]}' in case-insensitive alphabetical order");
            }
        });
    }
}
