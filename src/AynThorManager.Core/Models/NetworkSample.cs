namespace AynThorManager.Core.Models;

/// <summary>
/// A single telemetry reading of the link to the Thor. <see cref="LatencyMs"/> is the
/// round-trip of an adb echo; the Wi-Fi fields come from <c>dumpsys wifi</c> and are
/// null when they couldn't be read.
/// </summary>
public sealed record NetworkSample(
    int LatencyMs,
    int? RssiDbm,
    int? FrequencyMhz)
{
    public bool Is5GHz => FrequencyMhz is > 5000;
}
