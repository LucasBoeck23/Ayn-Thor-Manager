namespace AynThorManager.Core.Models;

public sealed record TransferFileResult(
    string FileName,
    bool Success,
    bool IntegrityVerified,
    string? ErrorMessage);
