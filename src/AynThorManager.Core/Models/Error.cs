namespace AynThorManager.Core.Models;

public sealed record Error(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object>? Details = null);
