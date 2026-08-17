namespace AynThorManager.Core.Models;

public sealed record AdbCommand(
    string Arguments,
    TimeSpan Timeout,
    string Description);
