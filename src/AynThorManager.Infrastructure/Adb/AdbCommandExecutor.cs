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

/// <summary>
/// Low-level ADB command executor using CliWrap.
/// Wraps all ADB CLI interactions with timeout, cancellation, and structured result handling.
/// </summary>
public sealed class AdbCommandExecutor(
    IOptions<AdbOptions> options,
    ILogger<AdbCommandExecutor> logger) : IAdbCommandExecutor
{
    private readonly string _adbPath = options.Value.AdbPath;

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(string arguments, TimeSpan timeout, CancellationToken ct)
    {
        logger.LogDebug("ADB: {Arguments} (timeout: {Timeout})", arguments, timeout);
        return ExecuteInternalAsync(arguments, timeout, ct);
    }

    /// <summary>
    /// Core execution method that handles timeout, cancellation, and error mapping.
    /// Single source of truth for all CliWrap interactions — eliminates duplication.
    /// </summary>
    private async Task<CommandResult> ExecuteInternalAsync(string arguments, TimeSpan timeout, CancellationToken ct)
    {
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
            stopwatch.Stop();
            logger.LogWarning("ADB command timed out after {Timeout}: {Arguments}", timeout, arguments);

            return new CommandResult(
                Success: false,
                StandardOutput: string.Empty,
                StandardError: $"Command timed out after {timeout.TotalSeconds:F0} seconds",
                ExitCode: -1,
                Duration: stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogDebug("ADB command cancelled: {Arguments}", arguments);

            return new CommandResult(
                Success: false,
                StandardOutput: string.Empty,
                StandardError: "Command was cancelled",
                ExitCode: -1,
                Duration: stopwatch.Elapsed);
        }
        catch (CommandExecutionException ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "ADB command execution failed: {Arguments}", arguments);

            return new CommandResult(
                Success: false,
                StandardOutput: string.Empty,
                StandardError: ex.Message,
                ExitCode: ex.ExitCode,
                Duration: stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Unexpected error executing ADB command: {Arguments}", arguments);

            return new CommandResult(
                Success: false,
                StandardOutput: string.Empty,
                StandardError: ex.Message,
                ExitCode: -1,
                Duration: stopwatch.Elapsed);
        }
    }
}
