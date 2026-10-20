namespace AynThorManager.Core.Models;

public sealed record AdbCommand(
    string Arguments,
    TimeSpan Timeout,
    string Description);

public sealed record CommandResult(
    bool Success,
    string StandardOutput,
    string StandardError,
    int ExitCode,
    TimeSpan Duration);
