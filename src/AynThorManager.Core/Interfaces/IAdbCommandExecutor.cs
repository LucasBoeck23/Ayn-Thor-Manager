using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

public interface IAdbCommandExecutor
{
    Task<CommandResult> ExecuteAsync(string arguments, TimeSpan timeout, CancellationToken ct);
}
