namespace AynThorManager.Infrastructure.Adb;

/// <summary>
/// Configuration options for the ADB executable.
/// </summary>
public sealed class AdbOptions
{
    /// <summary>
    /// Path to the ADB executable. Defaults to "adb" (resolves from PATH).
    /// </summary>
    public string AdbPath { get; set; } = "adb";
}
