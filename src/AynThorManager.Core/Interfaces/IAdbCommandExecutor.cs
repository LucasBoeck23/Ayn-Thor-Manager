using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Low-level wrapper over CliWrap for executing ADB commands.
/// </summary>
public interface IAdbCommandExecutor
{
    /// <summary>
    /// Executes an ADB command with the specified arguments.
    /// </summary>
    Task<CommandResult> ExecuteAsync(string arguments, TimeSpan timeout, CancellationToken ct);
}
