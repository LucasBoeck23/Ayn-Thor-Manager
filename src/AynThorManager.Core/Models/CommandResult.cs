namespace AynThorManager.Core.Models;

public sealed record CommandResult(
    bool Success,
    string StandardOutput,
    string StandardError,
    int ExitCode,
    TimeSpan Duration);
