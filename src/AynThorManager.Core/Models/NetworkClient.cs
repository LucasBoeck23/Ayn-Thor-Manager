namespace AynThorManager.Core.Models;

/// <summary>
/// A device seen on a local network interface (IP + hardware address). Used to
/// find the Thor once it joins the PC's direct-connection hotspot.
/// </summary>
public sealed record NetworkClient(string IpAddress, string MacAddress, bool IsReachable);

/// <summary>Outcome of a hotspot operation, with an error message on failure.</summary>
public readonly record struct HotspotResult(bool IsSuccess, string? Error)
{
    public static HotspotResult Ok() => new(true, null);
    public static HotspotResult Fail(string error) => new(false, error);
}
