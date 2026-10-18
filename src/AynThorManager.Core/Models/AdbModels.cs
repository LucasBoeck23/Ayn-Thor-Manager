namespace AynThorManager.Core.Models;

/// <summary>An adb invocation: the argument string, its timeout and a short label for logs.</summary>
public sealed record AdbCommand(
    string Arguments,
    TimeSpan Timeout,
    string Description);

/// <summary>The outcome of running an adb command.</summary>
public sealed record CommandResult(
    bool Success,
    string StandardOutput,
    string StandardError,
    int ExitCode,
    TimeSpan Duration);
