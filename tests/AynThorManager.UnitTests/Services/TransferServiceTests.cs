using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.Transfer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AynThorManager.UnitTests.Services;

public sealed class TransferServiceTests : IDisposable
{
    private readonly ICommandQueue _commandQueue = Substitute.For<ICommandQueue>();
    private readonly IAdbConnectionManager _connectionManager = Substitute.For<IAdbConnectionManager>();
    private readonly IWebSocketNotifier _notifier = Substitute.For<IWebSocketNotifier>();
    private readonly TransferService _sut;

    public TransferServiceTests()
    {
        _connectionManager.IsConnected.Returns(true);
        _sut = new TransferService(
            _commandQueue,
            _connectionManager,
            _notifier,
            NullLogger<TransferService>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    #region Helpers

    private static TransferRequest MakeRequest(int fileCount = 1, long fileSize = 1024 * 1024)
    {
        var files = Enumerable.Range(1, fileCount)
            .Select(i => new TransferFile($"C:\\files\\file{i}.zip", $"file{i}.zip", fileSize))
            .ToList();
        return new TransferRequest(files, "/sdcard/ROMs");
    }

    private static CommandResult MakeSuccessResult(string output = "") =>
        new(true, output, "", 0, TimeSpan.FromMilliseconds(100));

    private static CommandResult MakeFailureResult(string error = "error") =>
        new(false, "", error, 1, TimeSpan.FromMilliseconds(100));

    private void SetupSpaceCheckSuccess(long availableKb = 50_000_000)
    {
        // Match the df command pattern
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell df")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                MakeSuccessResult($"Filesystem     1K-blocks    Used Available Use% Mounted on\n/dev/fuse       58000000 30000000  {availableKb}  52% /storage/emulated")));
    }

    private void SetupPushSuccess()
    {
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("push")),
                CommandPriority.Bulk,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccessResult("1 file pushed.")));
    }

    private void SetupIntegrityCheckSuccess(long fileSize = 1024 * 1024)
    {
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell stat")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccessResult(fileSize.ToString())));
    }

    private void SetupFullSuccessFlow(int fileCount = 1, long fileSize = 1024 * 1024)
    {
        SetupSpaceCheckSuccess();
        SetupPushSuccess();
        SetupIntegrityCheckSuccess(fileSize);
    }

    #endregion

    #region Full Upload Flow (Req 3.1, 3.2, 3.3)

    [Fact]
    public async Task UploadAsync_SingleFileSuccess_ReturnsSuccessfulTransferResult()
    {
        // Arrange
        var request = MakeRequest(fileCount: 1);
        SetupFullSuccessFlow();

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Results.Should().HaveCount(1);
        result.Value.Results[0].Success.Should().BeTrue();
        result.Value.Results[0].IntegrityVerified.Should().BeTrue();
        result.Value.Results[0].FileName.Should().Be("file1.zip");
        result.Value.Results[0].ErrorMessage.Should().BeNull();
        result.Value.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task UploadAsync_MultipleFilesSuccess_AllFilesTransferredSequentially()
    {
        // Arrange
        var request = MakeRequest(fileCount: 3);
        SetupFullSuccessFlow();

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Results.Should().HaveCount(3);
        result.Value.Results.Should().AllSatisfy(r =>
        {
            r.Success.Should().BeTrue();
            r.IntegrityVerified.Should().BeTrue();
        });
    }

    [Fact]
    public async Task UploadAsync_Success_EmitsProgressUpdates()
    {
        // Arrange
        var request = MakeRequest(fileCount: 1);
        SetupFullSuccessFlow();

        var progressUpdates = new List<TransferProgress>();
        using var subscription = _sut.ProgressUpdates.Subscribe(p => progressUpdates.Add(p));

        // Act
        await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        progressUpdates.Should().NotBeEmpty();
        progressUpdates.Should().Contain(p => p.FileName == "file1.zip");
    }

    [Fact]
    public async Task UploadAsync_Success_NotifiesCompletion()
    {
        // Arrange
        var request = MakeRequest(fileCount: 1);
        SetupFullSuccessFlow();

        // Act
        await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        await _notifier.Received(1).SendTransferCompletedAsync(
            Arg.Any<TransferResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_Success_IsTransferInProgressFalseAfterCompletion()
    {
        // Arrange
        var request = MakeRequest(fileCount: 1);
        SetupFullSuccessFlow();

        // Act
        await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        _sut.IsTransferInProgress.Should().BeFalse();
    }

    #endregion

    #region Cancellation (Req 3.8)

    [Fact]
    public async Task UploadAsync_CancellationRequested_StopsAndReturnsPartialResults()
    {
        // Arrange
        var request = MakeRequest(fileCount: 3);
        SetupSpaceCheckSuccess();
        SetupIntegrityCheckSuccess();

        var cts = new CancellationTokenSource();

        // First push succeeds, second push triggers cancellation
        var pushCallCount = 0;
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("push")),
                CommandPriority.Bulk,
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                pushCallCount++;
                if (pushCallCount == 1)
                    return Result<CommandResult>.Success(MakeSuccessResult("1 file pushed."));

                // Cancel during second push — simulates user cancellation mid-transfer
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        // Setup rm -f for partial removal
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell rm -f")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccessResult()));

        // Act
        var result = await _sut.UploadAsync(request, cts.Token);

        // Assert — cancellation detected, first file preserved, batch stopped
        result.IsSuccess.Should().BeTrue();
        result.Value!.Results.Should().HaveCount(1);
        result.Value.Results[0].Success.Should().BeTrue();
        result.Value.Results[0].FileName.Should().Be("file1.zip");
    }

    [Fact]
    public async Task CancelCurrentTransferAsync_NoTransferInProgress_ReturnsFailure()
    {
        // Act
        var result = await _sut.CancelCurrentTransferAsync(CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("TRANSFER_NOT_IN_PROGRESS");
    }

    #endregion

    #region Mid-Batch Failure (Req 3.7)

    [Fact]
    public async Task UploadAsync_SecondFileFails_StopsBatchAndPreservesFirst()
    {
        // Arrange
        var request = MakeRequest(fileCount: 3);
        SetupSpaceCheckSuccess();
        SetupIntegrityCheckSuccess();

        var pushCallCount = 0;
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("push")),
                CommandPriority.Bulk,
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                pushCallCount++;
                if (pushCallCount <= 1)
                    return Result<CommandResult>.Success(MakeSuccessResult("1 file pushed."));

                // Second file fails
                return Result<CommandResult>.Success(MakeFailureResult("adb: error: failed to copy"));
            });

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("TRANSFER_FAILED");
        result.Error.Message.Should().Contain("file2.zip");

        // Notify should be called with failure
        await _notifier.Received(1).SendTransferFailedAsync(
            Arg.Any<TransferResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_IntegrityCheckFails_ReportsFailure()
    {
        // Arrange
        var request = MakeRequest(fileCount: 1, fileSize: 1000);
        SetupSpaceCheckSuccess();
        SetupPushSuccess();

        // Return a different size for integrity check
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell stat")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccessResult("500")));

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("TRANSFER_FAILED");
        result.Error.Message.Should().Contain("integridade");
    }

    #endregion

    #region Space Check (Req 3.4)

    [Fact]
    public async Task UploadAsync_InsufficientSpace_RejectedBeforeTransfer()
    {
        // Arrange — use files within per-file size limit but total exceeding available space
        var request = MakeRequest(fileCount: 5, fileSize: 3_000_000_000); // 5 files × 3GB = 15GB
        SetupSpaceCheckSuccess(availableKb: 10_000_000); // Only ~10 GB available

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INSUFFICIENT_SPACE");

        // Verify no push was attempted
        await _commandQueue.DidNotReceive().EnqueueAsync(
            Arg.Is<AdbCommand>(c => c.Arguments.Contains("push")),
            CommandPriority.Bulk,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_SpaceCheckFails_AllowsTransferToProceed()
    {
        // Arrange — df command fails but we still allow transfer
        var request = MakeRequest(fileCount: 1);

        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell df")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Failure(new Error("TIMEOUT", "timed out")));

        SetupPushSuccess();
        SetupIntegrityCheckSuccess();

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Connection Loss (Req 3.5)

    [Fact]
    public async Task UploadAsync_ConnectionLostMidTransfer_AbortsAndNotifies()
    {
        // Arrange
        var request = MakeRequest(fileCount: 2);
        SetupSpaceCheckSuccess();

        // Setup rm for partial removal
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell rm -f")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccessResult()));

        // First push succeeds
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("push")),
                CommandPriority.Bulk,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccessResult("1 file pushed.")));

        // Integrity check succeeds but disconnects after first file
        var integrityCallCount = 0;
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell stat")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                integrityCallCount++;
                if (integrityCallCount == 1)
                {
                    // After first integrity check, disconnect
                    _connectionManager.IsConnected.Returns(false);
                }
                return Result<CommandResult>.Success(MakeSuccessResult("1048576"));
            });

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("TRANSFER_FAILED");
        result.Error.Message.Should().Contain("Conexão ADB perdida");

        // Should attempt notification
        await _notifier.Received().SendTransferFailedAsync(
            Arg.Any<TransferResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_DeviceNotConnected_RejectedImmediately()
    {
        // Arrange
        _connectionManager.IsConnected.Returns(false);
        var request = MakeRequest(fileCount: 1);

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("DEVICE_NOT_CONNECTED");
    }

    #endregion

    #region File Limits (Req 3.6)

    [Fact]
    public async Task UploadAsync_MoreThan20Files_Rejected()
    {
        // Arrange
        var request = MakeRequest(fileCount: 21);

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("FILE_LIMIT_EXCEEDED");

        // Verify no commands were enqueued
        await _commandQueue.DidNotReceive().EnqueueAsync(
            Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_FileExceeds4GB_Rejected()
    {
        // Arrange
        var oversizedFile = new TransferFile("C:\\big.zip", "big.zip", 5L * 1024 * 1024 * 1024); // 5 GB
        var request = new TransferRequest([oversizedFile], "/sdcard/ROMs");

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("FILE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task UploadAsync_Exactly20Files_Accepted()
    {
        // Arrange
        var request = MakeRequest(fileCount: 20);
        SetupFullSuccessFlow();

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Results.Should().HaveCount(20);
    }

    [Fact]
    public async Task UploadAsync_FileExactly4GB_Accepted()
    {
        // Arrange
        var exactFile = new TransferFile("C:\\exact.zip", "exact.zip", 4L * 1024 * 1024 * 1024); // Exactly 4 GB
        var request = new TransferRequest([exactFile], "/sdcard/ROMs");
        SetupSpaceCheckSuccess(availableKb: 5_000_000_000);
        SetupPushSuccess();

        // Integrity check returns exact size
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell stat")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                MakeSuccessResult((4L * 1024 * 1024 * 1024).ToString())));

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Transfer Already In Progress (Req 3.9)

    [Fact]
    public async Task UploadAsync_TransferAlreadyInProgress_RejectsSecondUpload()
    {
        // Arrange
        var request = MakeRequest(fileCount: 1);
        SetupSpaceCheckSuccess();

        // Make the first transfer take a long time by having push never complete immediately
        var pushTcs = new TaskCompletionSource<Result<CommandResult>>();
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("push")),
                CommandPriority.Bulk,
                Arg.Any<CancellationToken>())
            .Returns(pushTcs.Task);

        // Start first upload (it will be stuck waiting for push)
        var firstUploadTask = _sut.UploadAsync(request, CancellationToken.None);

        // Give it a moment to enter the transfer state
        await Task.Delay(50);

        // Act: try a second upload while first is in progress
        var secondResult = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        secondResult.IsSuccess.Should().BeFalse();
        secondResult.Error!.Code.Should().Be("TRANSFER_IN_PROGRESS");

        // Clean up: complete the first upload
        pushTcs.SetResult(Result<CommandResult>.Success(MakeSuccessResult("1 file pushed.")));
        SetupIntegrityCheckSuccess();
        await firstUploadTask;
    }

    #endregion

    #region Integrity Verification (Req 3.3)

    [Fact]
    public async Task UploadAsync_IntegritySizeMismatch_ReportsFailure()
    {
        // Arrange
        var request = MakeRequest(fileCount: 1, fileSize: 2048);
        SetupSpaceCheckSuccess();
        SetupPushSuccess();

        // Return different size
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell stat")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccessResult("1024")));

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("TRANSFER_FAILED");
    }

    [Fact]
    public async Task UploadAsync_IntegrityCheckCommandFails_ReportsFailure()
    {
        // Arrange
        var request = MakeRequest(fileCount: 1);
        SetupSpaceCheckSuccess();
        SetupPushSuccess();

        // Integrity check command fails
        _commandQueue.EnqueueAsync(
                Arg.Is<AdbCommand>(c => c.Arguments.Contains("shell stat")),
                CommandPriority.Normal,
                Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Failure(new Error("TIMEOUT", "timed out")));

        // Act
        var result = await _sut.UploadAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("TRANSFER_FAILED");
    }

    #endregion

    #region ParseAvailableSpace

    [Fact]
    public void ParseAvailableSpace_ValidDfOutput_ReturnsCorrectBytes()
    {
        // Arrange
        var output = "Filesystem     1K-blocks    Used Available Use% Mounted on\n/dev/fuse       58000000 30000000  28000000  52% /storage/emulated";

        // Act
        var result = TransferService.ParseAvailableSpace(output);

        // Assert
        result.Should().Be(28000000L * 1024);
    }

    [Fact]
    public void ParseAvailableSpace_InvalidOutput_ReturnsNull()
    {
        // Act
        var result = TransferService.ParseAvailableSpace("invalid");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseAvailableSpace_EmptyOutput_ReturnsNull()
    {
        // Act
        var result = TransferService.ParseAvailableSpace("");

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
