using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Adb;

/// <summary>
/// Throttles concurrent adb invocations so a burst of commands can't spawn an
/// unbounded number of adb processes. Up to <see cref="MaxConcurrency"/> run at once;
/// the rest wait.
/// </summary>
public sealed class CommandQueue(
    IAdbCommandExecutor executor,
    ILogger<CommandQueue> logger) : ICommandQueue, IDisposable
{
    private const int MaxConcurrency = 2;

    private readonly SemaphoreSlim _semaphore = new(MaxConcurrency, MaxConcurrency);

    public async Task<Result<CommandResult>> EnqueueAsync(AdbCommand command, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await executor.ExecuteAsync(command.Arguments, command.Timeout, ct);
            logger.LogDebug("{Description} -> exit {ExitCode}", command.Description, result.ExitCode);
            return Result<CommandResult>.Success(result);
        }
        catch (OperationCanceledException)
        {
            return Result<CommandResult>.Failure(new Error("COMMAND_CANCELLED", $"Command '{command.Description}' was cancelled."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Description} failed", command.Description);
            return Result<CommandResult>.Failure(new Error("COMMAND_EXECUTION_FAILED", ex.Message));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
