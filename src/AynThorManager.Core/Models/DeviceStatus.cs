namespace AynThorManager.Core.Models;

public sealed record DeviceStatus(
    DeviceStatusType Status,
    string? IpAddress,
    string? Message,
    DateTimeOffset Timestamp);
