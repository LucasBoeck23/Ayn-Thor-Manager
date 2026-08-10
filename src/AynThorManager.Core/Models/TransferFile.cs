namespace AynThorManager.Core.Models;

public sealed record TransferFile(
    string LocalPath,
    string FileName,
    long SizeBytes);
