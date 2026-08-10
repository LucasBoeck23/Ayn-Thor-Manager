using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Adb;

/// <summary>
/// Priority-based command queue with semaphore concurrency control.
/// Normal: 2 concurrent | Bulk: 1 concurrent | Critical: no limit.
/// </summary>
public sealed class CommandQueue(
    IAdbCommandExecutor executor,
    ILogger<CommandQueue> logger) : ICommandQueue, IDisposable
{
    private readonly SemaphoreSlim _normalSemaphore = new(2, 2);
    private readonly SemaphoreSlim _bulkSemaphore = new(1, 1);

    public async Task<Result<CommandResult>> EnqueueAsync(
        AdbCommand command, CommandPriority priority, CancellationToken ct)
    {
        var semaphore = priority switch
        {
            CommandPriority.Critical => null,
            CommandPriority.Normal => _normalSemaphore,
            CommandPriority.Bulk => _bulkSemaphore,
            _ => throw new ArgumentOutOfRangeException(nameof(priority))
        };

        if (semaphore is not null)
            await semaphore.WaitAsync(ct);

        try
        {
            var result = await executor.ExecuteAsync(command.Arguments, command.Timeout, ct);
            logger.LogDebug("[{Priority}] {Description} → exit {ExitCode}", priority, command.Description, result.ExitCode);
            return Result<CommandResult>.Success(result);
        }
        catch (OperationCanceledException)
        {
            return Result<CommandResult>.Failure(new Error("COMMAND_CANCELLED", $"Command '{command.Description}' was cancelled."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Priority}] {Description} failed", priority, command.Description);
            return Result<CommandResult>.Failure(new Error("COMMAND_EXECUTION_FAILED", ex.Message));
        }
        finally
        {
            semaphore?.Release();
        }
    }

    public void Dispose()
    {
        _normalSemaphore.Dispose();
        _bulkSemaphore.Dispose();
    }
}
