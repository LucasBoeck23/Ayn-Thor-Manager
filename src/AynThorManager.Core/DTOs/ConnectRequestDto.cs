namespace AynThorManager.Core.DTOs;

/// <summary>
/// Request DTO for initiating an ADB connection to the device.
/// </summary>
public sealed record ConnectRequestDto(string IpAddress);
