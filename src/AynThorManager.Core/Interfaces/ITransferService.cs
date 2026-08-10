using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Manages file upload operations with real-time progress reporting.
/// </summary>
public interface ITransferService
{
    /// <summary>
    /// Uploads files to the device sequentially via ADB push.
    /// Validates file limits and available space before starting.
    /// </summary>
    /// <param name="request">Transfer request containing files and destination.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing transfer results or an error.</returns>
    Task<Result<TransferResult>> UploadAsync(TransferRequest request, CancellationToken ct);

    /// <summary>
    /// Cancels the current transfer in progress.
    /// Removes partial files and preserves completed files.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success or an error.</returns>
    Task<Result> CancelCurrentTransferAsync(CancellationToken ct);

    /// <summary>
    /// Gets whether a transfer is currently in progress.
    /// </summary>
    bool IsTransferInProgress { get; }

    /// <summary>
    /// Observable stream of transfer progress updates (emitted every 500ms during upload).
    /// </summary>
    IObservable<TransferProgress> ProgressUpdates { get; }
}
