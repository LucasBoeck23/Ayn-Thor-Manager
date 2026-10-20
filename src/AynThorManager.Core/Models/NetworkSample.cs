namespace AynThorManager.Core.Models;

public sealed record NetworkSample(
    int LatencyMs,
    int? RssiDbm,
    int? FrequencyMhz)
{
    public bool Is5GHz => FrequencyMhz is > 5000;
}
