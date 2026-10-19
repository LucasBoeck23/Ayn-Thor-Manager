using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Files;

namespace AynThorManager.Infrastructure.Tests.Files;

/// <summary>
/// Drives <see cref="LsOutputParser"/> — the first unit-testable point of the ADB file
/// gateway — with canned <c>ls -la</c> output, so the parsing rules (type char, permission
/// string, size, mtime, name, and the skipping of headers and <c>.</c>/<c>..</c>) are
/// exercised without touching ADB or the device.
/// </summary>
public class LsOutputParserTests
{
    [Fact]
    public void Parses_a_regular_file_line_into_all_fields()
    {
        var entry = LsOutputParser.ParseLine(
            "-rw-rw---- 1 u0_a123 sdcard_rw 12345 2024-01-15 13:45 report.txt");

        Assert.NotNull(entry);
        Assert.Equal("report.txt", entry!.Name);
        Assert.Equal(12345, entry.Size);
        Assert.Equal(ThorFileType.File, entry.Type);
        Assert.Equal("rw-rw----", entry.Permissions);
        Assert.NotNull(entry.ModifiedTime);
        Assert.Equal(2024, entry.ModifiedTime!.Value.Year);
        Assert.Equal(1, entry.ModifiedTime.Value.Month);
        Assert.Equal(15, entry.ModifiedTime.Value.Day);
        Assert.Equal(13, entry.ModifiedTime.Value.Hour);
        Assert.Equal(45, entry.ModifiedTime.Value.Minute);
    }

    [Fact]
    public void Leading_type_char_d_is_a_directory()
    {
        var entry = LsOutputParser.ParseLine(
            "drwxrwx--x 4 u0_a123 sdcard_rw 4096 2024-02-01 09:00 Download");

        Assert.NotNull(entry);
        Assert.Equal(ThorFileType.Directory, entry!.Type);
        Assert.Equal("Download", entry.Name);
        Assert.Equal("rwxrwx--x", entry.Permissions);
    }

    [Fact]
    public void Symlink_keeps_only_its_own_name_dropping_the_target()
    {
        var entry = LsOutputParser.ParseLine(
            "lrwxrwxrwx 1 root root 21 2023-11-02 08:10 sdcard -> /storage/self/primary");

        Assert.NotNull(entry);
        Assert.Equal(ThorFileType.Symlink, entry!.Type);
        Assert.Equal("sdcard", entry.Name);
    }

    [Fact]
    public void Unknown_type_char_maps_to_other()
    {
        var entry = LsOutputParser.ParseLine(
            "crw-rw-rw- 1 root root 1, 3 2024-01-01 00:00 null");

        Assert.NotNull(entry);
        Assert.Equal(ThorFileType.Other, entry!.Type);
    }

    [Fact]
    public void Name_with_spaces_is_kept_intact()
    {
        var entry = LsOutputParser.ParseLine(
            "-rw-rw---- 1 u0_a123 sdcard_rw 10 2024-03-03 12:00 my notes v2.txt");

        Assert.NotNull(entry);
        Assert.Equal("my notes v2.txt", entry!.Name);
    }

    [Fact]
    public void Non_numeric_size_falls_back_to_zero()
    {
        // Special/device nodes can render size as "1, 3"; the parser must not throw and
        // falls back to 0 rather than mis-reading a size.
        var entry = LsOutputParser.ParseLine(
            "crw-rw-rw- 1 root root 1, 3 2024-01-01 00:00 null");

        Assert.NotNull(entry);
        Assert.Equal(0, entry!.Size);
    }

    [Fact]
    public void Unparseable_mtime_yields_null_without_dropping_the_entry()
    {
        var entry = LsOutputParser.ParseLine(
            "-rw-rw---- 1 u0_a123 sdcard_rw 10 ??? ?? data.bin");

        Assert.NotNull(entry);
        Assert.Null(entry!.ModifiedTime);
        Assert.Equal("data.bin", entry.Name);
    }

    [Fact]
    public void Lines_with_too_few_columns_return_null()
    {
        Assert.Null(LsOutputParser.ParseLine("-rw-rw---- 1 u0_a123"));
        Assert.Null(LsOutputParser.ParseLine(""));
    }

    [Fact]
    public void Parse_skips_total_header_and_dot_entries()
    {
        var output =
            "total 48\n" +
            "drwxrwx--x 4 u0_a123 sdcard_rw 4096 2024-02-01 09:00 .\n" +
            "drwxrwx--x 6 root     root      4096 2024-02-01 09:00 ..\n" +
            "-rw-rw---- 1 u0_a123 sdcard_rw 12345 2024-01-15 13:45 report.txt\n" +
            "drwxrwx--x 2 u0_a123 sdcard_rw 4096 2024-01-16 10:00 Pictures\n";

        var entries = LsOutputParser.Parse(output);

        Assert.Equal(2, entries.Count);
        Assert.Equal("report.txt", entries[0].Name);
        Assert.Equal(ThorFileType.File, entries[0].Type);
        Assert.Equal("Pictures", entries[1].Name);
        Assert.Equal(ThorFileType.Directory, entries[1].Type);
    }

    [Fact]
    public void Parse_handles_crlf_line_endings()
    {
        var output =
            "total 8\r\n" +
            "-rw-rw---- 1 u0_a123 sdcard_rw 10 2024-01-15 13:45 a.txt\r\n";

        var entries = LsOutputParser.Parse(output);

        Assert.Single(entries);
        Assert.Equal("a.txt", entries[0].Name);
    }

    [Fact]
    public void Parse_of_empty_output_is_empty()
    {
        Assert.Empty(LsOutputParser.Parse(string.Empty));
        Assert.Empty(LsOutputParser.Parse("\n\n"));
    }
}
