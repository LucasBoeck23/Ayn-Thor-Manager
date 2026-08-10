namespace AynThorManager.Core.Models;

public sealed record TransferProgress(
    string FileName,
    long BytesTransferred,
    long TotalBytes,
    int PercentComplete,
    long SpeedBytesPerSecond,
    int CurrentFileIndex,
    int TotalFiles);
