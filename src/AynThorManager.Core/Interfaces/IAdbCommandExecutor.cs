using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

public interface IAdbCommandExecutor
{
    Task<CommandResult> ExecuteAsync(string arguments, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Runs adb with each argument passed verbatim (no shell tokenisation), for commands
    /// whose operands are literal local/remote paths — e.g. <c>push</c>/<c>pull</c>, which
    /// adb does not run through a device shell, so shell-style quoting would be taken
    /// literally and corrupt the path. Each element of <paramref name="arguments"/> becomes
    /// exactly one argv entry.
    /// </summary>
    Task<CommandResult> ExecuteAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct);
}
