using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.Adb;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AynThorManager.UnitTests.Services;

public sealed class CommandQueueTests : IDisposable
{
    private readonly IAdbCommandExecutor _executor = Substitute.For<IAdbCommandExecutor>();
    private readonly CommandQueue _sut;

    public CommandQueueTests()
    {
        _sut = new CommandQueue(_executor, NullLogger<CommandQueue>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    private static AdbCommand MakeCommand(string description = "test") =>
        new("shell echo test", TimeSpan.FromSeconds(5), description);

    private static CommandResult MakeSuccessResult() =>
        new(true, "ok", "", 0, TimeSpan.FromMilliseconds(50));

    private static CommandResult MakeFailedResult() =>
        new(false, "", "error", 1, TimeSpan.FromMilliseconds(50));

    [Fact]
    public async Task EnqueueAsync_CriticalPriority_BypassesSemaphore()
    {
        // Arrange: fill both Normal and Bulk semaphores with blocking commands
        var blockingTcs1 = new TaskCompletionSource<CommandResult>();
        var blockingTcs2 = new TaskCompletionSource<CommandResult>();
        var blockingTcs3 = new TaskCompletionSource<CommandResult>();

        var callCount = 0;
        _executor.ExecuteAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var count = Interlocked.Increment(ref callCount);
                return count switch
                {
                    1 => blockingTcs1.Task,
                    2 => blockingTcs2.Task,
                    3 => blockingTcs3.Task,
                    _ => Task.FromResult(MakeSuccessResult())
                };
            });

        // Fill Normal semaphore (2 slots)
        var normalTask1 = _sut.EnqueueAsync(MakeCommand("normal1"), CommandPriority.Normal, CancellationToken.None);
        var normalTask2 = _sut.EnqueueAsync(MakeCommand("normal2"), CommandPriority.Normal, CancellationToken.None);

        // Fill Bulk semaphore (1 slot)
        var bulkTask = _sut.EnqueueAsync(MakeCommand("bulk1"), CommandPriority.Bulk, CancellationToken.None);

        // Give time for semaphores to be acquired
        await Task.Delay(50);

        // Act: Critical command should bypass semaphore and execute immediately
        var criticalTask = _sut.EnqueueAsync(MakeCommand("critical"), CommandPriority.Critical, CancellationToken.None);

        // Allow a short delay for the critical command to start executing
        await Task.Delay(50);

        // Assert: Critical task should have invoked the executor (callCount >= 4 means it didn't wait)
        callCount.Should().BeGreaterThanOrEqualTo(4,
            "Critical priority should bypass semaphore and execute immediately even when Normal and Bulk slots are full");

        // Cleanup
        blockingTcs1.SetResult(MakeSuccessResult());
        blockingTcs2.SetResult(MakeSuccessResult());
        blockingTcs3.SetResult(MakeSuccessResult());

        await Task.WhenAll(normalTask1, normalTask2, bulkTask, criticalTask);
    }

    [Fact]
    public async Task EnqueueAsync_NormalPriority_LimitsTo2Concurrent()
    {
        // Arrange: use TaskCompletionSource to control when each command finishes
        var tcs1 = new TaskCompletionSource<CommandResult>();
        var tcs2 = new TaskCompletionSource<CommandResult>();
        var tcs3 = new TaskCompletionSource<CommandResult>();

        var callCount = 0;
        _executor.ExecuteAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var count = Interlocked.Increment(ref callCount);
                return count switch
                {
                    1 => tcs1.Task,
                    2 => tcs2.Task,
                    3 => tcs3.Task,
                    _ => Task.FromResult(MakeSuccessResult())
                };
            });

        // Act: enqueue 3 Normal priority commands
        var task1 = _sut.EnqueueAsync(MakeCommand("n1"), CommandPriority.Normal, CancellationToken.None);
        var task2 = _sut.EnqueueAsync(MakeCommand("n2"), CommandPriority.Normal, CancellationToken.None);
        var task3 = _sut.EnqueueAsync(MakeCommand("n3"), CommandPriority.Normal, CancellationToken.None);

        // Wait for the first two to start executing
        await Task.Delay(100);

        // Assert: only 2 commands should be executing (third is waiting for semaphore)
        callCount.Should().Be(2, "Normal priority allows max 2 concurrent commands");

        // Release one slot
        tcs1.SetResult(MakeSuccessResult());
        await Task.Delay(50);

        // Now the third should have started
        callCount.Should().Be(3, "Third command should start after a slot is released");

        // Cleanup
        tcs2.SetResult(MakeSuccessResult());
        tcs3.SetResult(MakeSuccessResult());

        await Task.WhenAll(task1, task2, task3);
    }

    [Fact]
    public async Task EnqueueAsync_BulkPriority_LimitsTo1Concurrent()
    {
        // Arrange
        var tcs1 = new TaskCompletionSource<CommandResult>();
        var tcs2 = new TaskCompletionSource<CommandResult>();

        var callCount = 0;
        _executor.ExecuteAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var count = Interlocked.Increment(ref callCount);
                return count switch
                {
                    1 => tcs1.Task,
                    2 => tcs2.Task,
                    _ => Task.FromResult(MakeSuccessResult())
                };
            });

        // Act: enqueue 2 Bulk priority commands
        var task1 = _sut.EnqueueAsync(MakeCommand("b1"), CommandPriority.Bulk, CancellationToken.None);
        var task2 = _sut.EnqueueAsync(MakeCommand("b2"), CommandPriority.Bulk, CancellationToken.None);

        // Wait for the first to start
        await Task.Delay(100);

        // Assert: only 1 Bulk command should be executing
        callCount.Should().Be(1, "Bulk priority allows max 1 concurrent command");

        // Release the first slot
        tcs1.SetResult(MakeSuccessResult());
        await Task.Delay(50);

        // Now the second should have started
        callCount.Should().Be(2, "Second bulk command should start after first completes");

        // Cleanup
        tcs2.SetResult(MakeSuccessResult());
        await Task.WhenAll(task1, task2);
    }

    [Fact]
    public async Task EnqueueAsync_CancellationToken_ReturnsCancelledResult()
    {
        // Arrange: executor will throw OperationCanceledException (simulating cancellation during execution)
        _executor.ExecuteAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<CommandResult>(ci =>
            {
                throw new OperationCanceledException("The operation was canceled.");
            });

        // Act
        var result = await _sut.EnqueueAsync(MakeCommand("cancel-test"), CommandPriority.Normal, CancellationToken.None);

        // Assert: cancellation during execution is caught and returns a failure Result
        result.IsSuccess.Should().BeFalse("cancellation during execution should return failure");
        result.Error!.Code.Should().Be("COMMAND_CANCELLED");
    }

    [Fact]
    public async Task EnqueueAsync_CommandSucceeds_ReturnsSuccessResult()
    {
        // Arrange
        var expectedResult = MakeSuccessResult();
        _executor.ExecuteAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        // Act
        var result = await _sut.EnqueueAsync(MakeCommand(), CommandPriority.Normal, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResult);
    }

    [Fact]
    public async Task EnqueueAsync_CommandFails_StillReturnsSuccessWithFailedCommandResult()
    {
        // Arrange: executor returns a CommandResult with Success=false (command ran but returned error exit code)
        var failedCommandResult = MakeFailedResult();
        _executor.ExecuteAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failedCommandResult));

        // Act
        var result = await _sut.EnqueueAsync(MakeCommand(), CommandPriority.Normal, CancellationToken.None);

        // Assert: The Result<> wrapper is Success (the queue processed it fine),
        // even though the inner CommandResult.Success is false
        result.IsSuccess.Should().BeTrue(
            "the queue should wrap the result as Success even when the command itself failed");
        result.Value!.Success.Should().BeFalse(
            "the inner CommandResult should reflect the actual command failure");
        result.Value.ExitCode.Should().Be(1);
    }
}
