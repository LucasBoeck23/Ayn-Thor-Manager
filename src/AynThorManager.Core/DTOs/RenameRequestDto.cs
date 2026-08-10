namespace AynThorManager.Core.DTOs;

/// <summary>
/// Request DTO for renaming a file or directory on the device.
/// </summary>
/// <param name="CurrentPath">The current full path of the item to rename.</param>
/// <param name="NewName">The new name for the item (name only, no path separators).</param>
public sealed record RenameRequestDto(string CurrentPath, string NewName);
