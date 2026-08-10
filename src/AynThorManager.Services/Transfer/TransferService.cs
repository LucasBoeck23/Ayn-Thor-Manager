using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Core.Validators;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Transfer;

/// <summary>
/// Manages file upload operations with real-time progress and integrity verification.
/// Handles cancellation, connection loss, and individual file failures according to requirements 3.1–3.9.
/// </summary>
public sealed class TransferService(
    ICommandQueue commandQueue,
    IAdbConnectionManager connectionManager,
    IWebSocketNotifier notifier,
    ILogger<TransferService> logger) : ITransferService, IDisposable
{
    private static readonly TimeSpan FileTransferTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SpaceCheckTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan IntegrityCheckTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PartialRemovalTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    private readonly Subject<TransferProgress> _progressSubject = new();
    private readonly SemaphoreSlim _transferLock = new(1, 1);
    private CancellationTokenSource? _currentTransferCts;
    private volatile bool _isTransferInProgress;

    /// <inheritdoc />
    public bool IsTransferInProgress => _isTransferInProgress;

    /// <inheritdoc />
    public IObservable<TransferProgress> ProgressUpdates => _progressSubject.AsObservable();

    /// <inheritdoc />
    public async Task<Result<TransferResult>> UploadAsync(TransferRequest request, CancellationToken ct)
    {
        // 1. Reject if transfer already in progress (Req 3.9)
        if (_isTransferInProgress)
        {
            logger.LogWarning("Upload rejected: transfer already in progress");
            return Result<TransferResult>.Failure(new Error(
                "TRANSFER_IN_PROGRESS",
                "Já existe uma transferência em andamento."));
        }

        // 2. Validate file limits (Req 3.6)
        var limitsValidation = TransferValidator.ValidateFileLimits(request.Files);
        if (!limitsValidation.IsSuccess)
            return Result<TransferResult>.Failure(limitsValidation.Error!);

        // 3. Check device connection
        if (!connectionManager.IsConnected)
        {
            logger.LogWarning("Upload rejected: device not connected");
            return Result<TransferResult>.Failure(new Error(
                "DEVICE_NOT_CONNECTED",
                "O dispositivo não está conectado via ADB."));
        }

        // 4. Check available space (Req 3.4)
        var spaceCheck = await CheckAvailableSpaceAsync(request, ct);
        if (!spaceCheck.IsSuccess)
            return Result<TransferResult>.Failure(spaceCheck.Error!);

        // 5. Acquire transfer lock and start
        await _transferLock.WaitAsync(ct);
        try
        {
            _isTransferInProgress = true;
            _currentTransferCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            return await ExecuteTransferAsync(request, _currentTransferCts.Token);
        }
        finally
        {
            _isTransferInProgress = false;
            _currentTransferCts?.Dispose();
            _currentTransferCts = null;
            _transferLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result> CancelCurrentTransferAsync(CancellationToken ct)
    {
        if (!_isTransferInProgress || _currentTransferCts is null)
        {
            return Result.Failure(new Error(
                "TRANSFER_NOT_IN_PROGRESS",
                "Nenhuma transferência em andamento para cancelar."));
        }

        logger.LogInformation("Cancellation requested for current transfer");
        await _currentTransferCts.CancelAsync();
        return Result.Success();
    }

    private async Task<Result<TransferResult>> ExecuteTransferAsync(TransferRequest request, CancellationToken ct)
    {
        var startTime = Stopwatch.GetTimestamp();
        var results = new List<TransferFileResult>();
        var totalFiles = request.Files.Count;

        // Subscribe to connection status changes to detect mid-transfer disconnection (Req 3.5)
        using var connectionLostCts = new CancellationTokenSource();
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, connectionLostCts.Token);
        var connectionLost = false;

        using var statusSubscription = connectionManager.StatusChanges
            .Where(s => s.Status == DeviceStatusType.Disconnected)
            .Take(1)
            .Subscribe(_ =>
            {
                connectionLost = true;
                connectionLostCts.Cancel();
            });

        for (var i = 0; i < totalFiles; i++)
        {
            var file = request.Files[i];
            var remotePath = $"{request.DestinationPath.TrimEnd('/')}/{file.FileName}";

            // Check cancellation before starting each file (Req 3.8)
            if (ct.IsCancellationRequested)
            {
                logger.LogInformation("Transfer cancelled before file {Index}/{Total}", i + 1, totalFiles);
                await NotifyTransferCancelledAsync(results, totalFiles);
                var duration = GetElapsed(startTime);
                return Result<TransferResult>.Success(new TransferResult(results, duration));
            }

            // Check connection loss before starting each file (Req 3.5)
            if (connectionLost || !connectionManager.IsConnected)
            {
                logger.LogWarning("Connection lost before transfer of file {FileName}", file.FileName);
                await HandleConnectionLossAsync(remotePath, file.FileName, results, startTime);
                return Result<TransferResult>.Failure(new Error(
                    "TRANSFER_FAILED",
                    $"Conexão ADB perdida durante transferência de '{file.FileName}'."));
            }

            // Transfer file via ADB push (Req 3.1)
            var transferResult = await TransferSingleFileAsync(
                file, remotePath, i, totalFiles, combinedCts.Token);

            // Handle cancellation mid-file (Req 3.8)
            if (ct.IsCancellationRequested)
            {
                logger.LogInformation("Transfer cancelled during file {FileName}", file.FileName);
                await TryRemovePartialFileAsync(remotePath);
                await NotifyTransferCancelledAsync(results, totalFiles);
                var duration = GetElapsed(startTime);
                return Result<TransferResult>.Success(new TransferResult(results, duration));
            }

            // Handle connection loss mid-file (Req 3.5)
            if (connectionLost)
            {
                logger.LogWarning("Connection lost during transfer of file {FileName}", file.FileName);
                await HandleConnectionLossAsync(remotePath, file.FileName, results, startTime);
                return Result<TransferResult>.Failure(new Error(
                    "TRANSFER_FAILED",
                    $"Conexão ADB perdida durante transferência de '{file.FileName}'."));
            }

            if (!transferResult.Success)
            {
                // Mid-batch failure (Req 3.7): stop batch, preserve completed files, notify
                logger.LogWarning("File transfer failed: {FileName} — {Error}", file.FileName, transferResult.ErrorMessage);
                results.Add(transferResult);
                await NotifyTransferFailedAsync(results, GetElapsed(startTime));
                return Result<TransferResult>.Failure(new Error(
                    "TRANSFER_FAILED",
                    $"Falha na transferência do arquivo '{file.FileName}': {transferResult.ErrorMessage}"));
            }

            results.Add(transferResult);
        }

        var totalDuration = GetElapsed(startTime);
        var finalResult = new TransferResult(results, totalDuration);

        logger.LogInformation("Transfer completed: {FileCount} files in {Duration}", results.Count, totalDuration);
        await notifier.SendTransferCompletedAsync(finalResult, CancellationToken.None);

        return Result<TransferResult>.Success(finalResult);
    }

    private async Task<TransferFileResult> TransferSingleFileAsync(
        TransferFile file, string remotePath, int fileIndex, int totalFiles, CancellationToken ct)
    {
        try
        {
            // Execute push command via CommandQueue with Bulk priority
            var pushCommand = new AdbCommand(
                Arguments: $"push \"{file.LocalPath}\" \"{remotePath}\"",
                Timeout: FileTransferTimeout,
                Description: $"push {file.FileName}");

            // Start progress timer to emit progress every 500ms (Req 3.2)
            var transferStartTime = Stopwatch.GetTimestamp();
            long lastBytesReported = 0;

            using var progressTimer = new PeriodicTimer(ProgressInterval);
            using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // Fire initial progress
            EmitProgress(file.FileName, 0, file.SizeBytes, 0, fileIndex, totalFiles, transferStartTime);

            // Start progress emission background task
            var progressTask = EmitPeriodicProgressAsync(
                progressTimer, file, fileIndex, totalFiles, transferStartTime, () => lastBytesReported, progressCts.Token);

            // Execute the push command
            var result = await commandQueue.EnqueueAsync(pushCommand, CommandPriority.Bulk, ct);

            // Stop progress timer
            await progressCts.CancelAsync();
            try { await progressTask; } catch (OperationCanceledException) { }

            if (ct.IsCancellationRequested)
            {
                return new TransferFileResult(file.FileName, false, false, "Transferência cancelada pelo usuário.");
            }

            if (!result.IsSuccess)
            {
                return new TransferFileResult(file.FileName, false, false, result.Error!.Message);
            }

            if (!result.Value!.Success)
            {
                return new TransferFileResult(file.FileName, false, false,
                    $"ADB push falhou: {result.Value.StandardError}");
            }

            // Emit final progress (100%)
            EmitProgress(file.FileName, file.SizeBytes, file.SizeBytes, 0, fileIndex, totalFiles, transferStartTime);

            // Verify integrity (Req 3.3)
            var integrityVerified = await VerifyFileIntegrityAsync(remotePath, file.SizeBytes, ct);

            if (!integrityVerified)
            {
                return new TransferFileResult(file.FileName, false, false,
                    "Verificação de integridade falhou: tamanho no destino difere do original.");
            }

            return new TransferFileResult(file.FileName, true, true, null);
        }
        catch (OperationCanceledException)
        {
            return new TransferFileResult(file.FileName, false, false, "Transferência cancelada pelo usuário.");
        }
    }

    private async Task EmitPeriodicProgressAsync(
        PeriodicTimer timer,
        TransferFile file,
        int fileIndex,
        int totalFiles,
        long transferStartTimestamp,
        Func<long> getBytesTransferred,
        CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var bytes = getBytesTransferred();
                EmitProgress(file.FileName, bytes, file.SizeBytes, bytes, fileIndex, totalFiles, transferStartTimestamp);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when transfer completes or is cancelled
        }
    }

    private void EmitProgress(
        string fileName, long bytesTransferred, long totalBytes, long previousBytes,
        int fileIndex, int totalFiles, long transferStartTimestamp)
    {
        var percent = totalBytes > 0 ? (int)(bytesTransferred * 100 / totalBytes) : 0;
        var elapsedSeconds = Stopwatch.GetElapsedTime(transferStartTimestamp).TotalSeconds;
        var speed = elapsedSeconds > 0 ? (long)(bytesTransferred / elapsedSeconds) : 0L;

        var progress = new TransferProgress(
            fileName, bytesTransferred, totalBytes, percent, speed, fileIndex + 1, totalFiles);

        _progressSubject.OnNext(progress);
    }

    private async Task<bool> VerifyFileIntegrityAsync(string remotePath, long expectedSize, CancellationToken ct)
    {
        var command = new AdbCommand(
            Arguments: $"shell stat -c %s \"{remotePath}\"",
            Timeout: IntegrityCheckTimeout,
            Description: $"integrity check {remotePath}");

        var result = await commandQueue.EnqueueAsync(command, CommandPriority.Normal, ct);

        if (!result.IsSuccess || !result.Value!.Success)
            return false;

        var output = result.Value.StandardOutput.Trim();
        return long.TryParse(output, out var actualSize) && actualSize == expectedSize;
    }

    private async Task<Result> CheckAvailableSpaceAsync(TransferRequest request, CancellationToken ct)
    {
        var totalSize = request.Files.Sum(f => f.SizeBytes);

        var command = new AdbCommand(
            Arguments: $"shell df \"{request.DestinationPath}\"",
            Timeout: SpaceCheckTimeout,
            Description: "space check");

        var result = await commandQueue.EnqueueAsync(command, CommandPriority.Normal, ct);

        if (!result.IsSuccess || !result.Value!.Success)
        {
            logger.LogWarning("Space check failed, allowing transfer to proceed");
            return Result.Success(); // Allow transfer if space check fails (device may not support df)
        }

        var availableSpace = ParseAvailableSpace(result.Value.StandardOutput);
        if (availableSpace.HasValue)
        {
            return TransferValidator.ValidateAvailableSpace(totalSize, availableSpace.Value);
        }

        return Result.Success();
    }

    /// <summary>
    /// Parses the output of `adb shell df` to extract available space in bytes.
    /// </summary>
    public static long? ParseAvailableSpace(string dfOutput)
    {
        // Parse output of 'adb shell df /sdcard'
        // Format:
        // Filesystem     1K-blocks    Used Available Use% Mounted on
        // /dev/fuse       58000000 30000000  28000000  52% /storage/emulated
        var lines = dfOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return null;

        // Parse data line (skip header)
        var parts = lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return null;

        // Available is typically the 4th column (index 3), in 1K-blocks
        if (long.TryParse(parts[3], out var availableKb))
            return availableKb * 1024; // Convert to bytes

        return null;
    }

    private async Task TryRemovePartialFileAsync(string remotePath)
    {
        try
        {
            var command = new AdbCommand(
                Arguments: $"shell rm -f \"{remotePath}\"",
                Timeout: PartialRemovalTimeout,
                Description: $"remove partial {remotePath}");

            await commandQueue.EnqueueAsync(command, CommandPriority.Normal, CancellationToken.None);
            logger.LogInformation("Partial file removed: {RemotePath}", remotePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove partial file: {RemotePath}", remotePath);
        }
    }

    private async Task HandleConnectionLossAsync(
        string remotePath, string fileName, IReadOnlyList<TransferFileResult> completedResults, long startTimestamp)
    {
        // Attempt to remove partial file with timeout (Req 3.5)
        await TryRemovePartialFileAsync(remotePath);

        // Notify via WebSocket
        try
        {
            var failResult = new TransferResult(
                [new TransferFileResult(fileName, false, false, "Conexão ADB perdida durante transferência.")],
                GetElapsed(startTimestamp));
            await notifier.SendTransferFailedAsync(failResult, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send connection lost notification");
        }
    }

    private async Task NotifyTransferCancelledAsync(IReadOnlyList<TransferFileResult> completedFiles, int totalFiles)
    {
        try
        {
            var result = new TransferResult(completedFiles, TimeSpan.Zero);
            await notifier.SendTransferFailedAsync(result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send cancellation notification");
        }
    }

    private async Task NotifyTransferFailedAsync(IReadOnlyList<TransferFileResult> results, TimeSpan duration)
    {
        try
        {
            var failResult = new TransferResult(results, duration);
            await notifier.SendTransferFailedAsync(failResult, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send transfer failure notification");
        }
    }

    private static TimeSpan GetElapsed(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp);

    public void Dispose()
    {
        _currentTransferCts?.Dispose();
        _progressSubject.Dispose();
        _transferLock.Dispose();
    }
}
