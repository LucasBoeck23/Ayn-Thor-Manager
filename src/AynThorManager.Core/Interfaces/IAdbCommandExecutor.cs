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
    /// <param name="arguments">ADB command arguments (e.g., "connect 192.168.1.100:5555").</param>
    /// <param name="timeout">Maximum execution time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command execution result.</returns>
    Task<CommandResult> ExecuteAsync(string arguments, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Executes a shell command on the device via "adb shell".
    /// </summary>
    /// <param name="shellCommand">Shell command to execute on the device.</param>
    /// <param name="timeout">Maximum execution time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command execution result.</returns>
    Task<CommandResult> ExecuteShellAsync(string shellCommand, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Pushes a local file to the device via "adb push".
    /// </summary>
    /// <param name="localPath">Path to the local file.</param>
    /// <param name="remotePath">Destination path on the device.</param>
    /// <param name="progress">Optional progress reporter for bytes transferred.</param>
    /// <param name="timeout">Maximum execution time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command execution result.</returns>
    Task<CommandResult> PushAsync(string localPath, string remotePath, IProgress<long>? progress, TimeSpan timeout, CancellationToken ct);
}
