namespace AynThorManager.Core.Models;

public sealed record TransferRequest(
    IReadOnlyList<TransferFile> Files,
    string DestinationPath);
