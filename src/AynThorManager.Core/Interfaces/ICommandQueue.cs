using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Priority-based command queue with concurrency control via semaphores.
/// </summary>
public interface ICommandQueue
{
    /// <summary>
    /// Enqueues an ADB command for execution with the specified priority.
    /// Critical priority bypasses semaphore; Normal allows 2 concurrent; Bulk allows 1 concurrent.
    /// </summary>
    /// <param name="command">The ADB command to execute.</param>
    /// <param name="priority">Execution priority level.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the command output or an error.</returns>
    Task<Result<CommandResult>> EnqueueAsync(AdbCommand command, CommandPriority priority, CancellationToken ct);
}
