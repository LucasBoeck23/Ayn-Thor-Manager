namespace AynThorManager.Core.DTOs;

/// <summary>
/// Request DTO for creating a new directory on the device.
/// </summary>
/// <param name="ParentPath">The parent directory path where the new directory will be created.</param>
/// <param name="Name">The name of the new directory.</param>
public sealed record CreateDirectoryRequestDto(string ParentPath, string Name);
