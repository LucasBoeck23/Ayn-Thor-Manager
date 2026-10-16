using System.Diagnostics;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using CommandResult = AynThorManager.Core.Models.CommandResult;

namespace AynThorManager.Infrastructure.Adb;

public sealed class AdbCommandExecutor(
    IOptions<AdbOptions> options,
    ILogger<AdbCommandExecutor> logger) : IAdbCommandExecutor
{
    private readonly string _adbPath = options.Value.AdbPath;

    public async Task<CommandResult> ExecuteAsync(string arguments, TimeSpan timeout, CancellationToken ct)
    {
        logger.LogDebug("ADB: {Arguments} (timeout: {Timeout})", arguments, timeout);

        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var result = await Cli.Wrap(_adbPath)
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(linkedCts.Token);

            stopwatch.Stop();

            var commandResult = new CommandResult(
                Success: result.ExitCode == 0,
                StandardOutput: result.StandardOutput,
                StandardError: result.StandardError,
                ExitCode: result.ExitCode,
                Duration: stopwatch.Elapsed);

            logger.LogDebug(
                "ADB command completed: ExitCode={ExitCode}, Duration={Duration}ms",
                commandResult.ExitCode, commandResult.Duration.TotalMilliseconds);

            return commandResult;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("ADB command timed out after {Timeout}: {Arguments}", timeout, arguments);
            return Failure(stopwatch, $"Command timed out after {timeout.TotalSeconds:F0} seconds", -1);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("ADB command cancelled: {Arguments}", arguments);
            return Failure(stopwatch, "Command was cancelled", -1);
        }
        catch (CommandExecutionException ex)
        {
            logger.LogError(ex, "ADB command execution failed: {Arguments}", arguments);
            return Failure(stopwatch, ex.Message, ex.ExitCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error executing ADB command: {Arguments}", arguments);
            return Failure(stopwatch, ex.Message, -1);
        }
    }

    private static CommandResult Failure(Stopwatch stopwatch, string standardError, int exitCode)
    {
        stopwatch.Stop();
        return new CommandResult(
            Success: false,
            StandardOutput: string.Empty,
            StandardError: standardError,
            ExitCode: exitCode,
            Duration: stopwatch.Elapsed);
    }
}
