using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

public interface ICommandQueue
{
    Task<Result<CommandResult>> EnqueueAsync(AdbCommand command, CancellationToken ct);
}
