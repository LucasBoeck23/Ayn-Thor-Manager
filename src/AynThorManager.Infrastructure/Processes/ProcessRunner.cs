using CliWrap;
using CliWrap.Buffered;

namespace AynThorManager.Infrastructure.Processes;

internal readonly record struct CommandOutput(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal delegate Task<CommandOutput> CommandRunner(
    string file, IReadOnlyList<string> args, CancellationToken ct);

internal static class ProcessRunner
{
    public static async Task<CommandOutput> RunAsync(
        string file, IReadOnlyList<string> args, CancellationToken ct = default)
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

    public static async Task<string?> WhichAsync(
        CommandRunner run, IReadOnlyList<string> candidates, CancellationToken ct = default)
    {
        foreach (var candidate in candidates)
        {
            var result = await run("which", [candidate], ct);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                return candidate;
        }

        return null;
    }
}
