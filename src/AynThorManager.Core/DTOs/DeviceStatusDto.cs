namespace AynThorManager.Core.DTOs;

/// <summary>
/// Response DTO representing the current device connection status.
/// </summary>
public sealed record DeviceStatusDto(string Status, string? IpAddress, string? Message);
