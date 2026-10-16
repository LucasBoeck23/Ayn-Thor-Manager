namespace AynThorManager.Core.Models;

public sealed record NetworkClient(string IpAddress, string MacAddress, bool IsReachable);

public readonly record struct HotspotResult(bool IsSuccess, string? Error)
{
    public static HotspotResult Ok() => new(true, null);
    public static HotspotResult Fail(string error) => new(false, error);
}
