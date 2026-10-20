using System.Diagnostics;
using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Files;

public sealed class FileManagerLauncher : IFileManagerLauncher
{
    private readonly CommandRunner _run;

    public FileManagerLauncher() : this(RealRun)
    {
    }

    internal FileManagerLauncher(CommandRunner run) => _run = run;

    public async Task OpenAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A mount path is required to open the file manager.", nameof(path));

        var result = await _run("xdg-open", new[] { path }, ct);
        if (result.ExitCode == 0)
            return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });

            if (process is not null)
                return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not open the file manager at '{path}'.", ex);
        }

        throw new InvalidOperationException(
            $"Could not open the file manager at '{path}'. " +
            (string.IsNullOrWhiteSpace(result.StandardError) ? null : result.StandardError));
    }

    private static async Task<CommandOutput> RealRun(string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            var result = await Cli.Wrap(file)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            return new CommandOutput(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (Exception ex)
        {
            return new CommandOutput(-1, string.Empty, ex.Message);
        }
    }
}

internal delegate Task<CommandOutput> CommandRunner(
    string file, IReadOnlyList<string> args, CancellationToken ct);

internal readonly record struct CommandOutput(
    int ExitCode,
    string StandardOutput,
    string StandardError);
