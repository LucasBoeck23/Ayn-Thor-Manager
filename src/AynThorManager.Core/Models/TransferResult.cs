namespace AynThorManager.Core.Models;

public sealed record TransferResult(
    IReadOnlyList<TransferFileResult> Results,
    TimeSpan TotalDuration);
