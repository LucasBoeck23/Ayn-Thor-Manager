using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.Adb;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AynThorManager.UnitTests.Services;

public sealed class AdbConnectionServiceTests : IDisposable
{
    private readonly ICommandQueue _commandQueue = Substitute.For<ICommandQueue>();
    private readonly AdbConnectionService _sut;

    public AdbConnectionServiceTests()
    {
        _sut = new AdbConnectionService(_commandQueue, NullLogger<AdbConnectionService>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    private static CommandResult MakeConnectedResult() =>
        new(true, "connected to 192.168.1.100:5555", "", 0, TimeSpan.Zero);

    private static CommandResult MakeUnauthorizedResult() =>
        new(true, "unauthorized", "", 0, TimeSpan.Zero);

    private static Result<CommandResult> MakeTimeoutFailure() =>
        Result<CommandResult>.Failure(new Error("TIMEOUT", "timed out"));

    [Fact]
    public async Task ConnectAsync_ValidIp_ReturnsConnectedStatus()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeConnectedResult()));

        // Act
        var result = await _sut.ConnectAsync("192.168.1.100", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(DeviceStatusType.Connected);
        result.Value.IpAddress.Should().Be("192.168.1.100");
        _sut.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectAsync_InvalidIp_ReturnsInvalidIpError()
    {
        // Act
        var result = await _sut.ConnectAsync("999.999.999.999", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INVALID_IP_FORMAT");
        _sut.IsConnected.Should().BeFalse();

        // Verify no command was enqueued
        await _commandQueue.DidNotReceive()
            .EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectAsync_Timeout_ReturnsTimeoutError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>())
            .Returns(MakeTimeoutFailure());

        // Act
        var result = await _sut.ConnectAsync("192.168.1.100", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CONNECTION_TIMEOUT");
        _sut.IsConnected.Should().BeFalse();
        _sut.CurrentStatus.Status.Should().Be(DeviceStatusType.Disconnected);
    }

    [Fact]
    public async Task ConnectAsync_Unauthorized_ReturnsUnauthorizedStatus()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeUnauthorizedResult()));

        // Act
        var result = await _sut.ConnectAsync("192.168.1.100", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(DeviceStatusType.Unauthorized);
        result.Value.IpAddress.Should().Be("192.168.1.100");
        _sut.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectAsync_AlreadyConnected_ReturnsAlreadyActiveError()
    {
        // Arrange: first connect successfully
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeConnectedResult()));

        await _sut.ConnectAsync("192.168.1.100", CancellationToken.None);
        _sut.IsConnected.Should().BeTrue();

        // Act: try connecting again
        var result = await _sut.ConnectAsync("192.168.1.200", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CONNECTION_ALREADY_ACTIVE");
    }

    [Fact]
    public async Task DisconnectAsync_ReturnsDisconnectedStatus()
    {
        // Arrange: connect first
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeConnectedResult()));

        await _sut.ConnectAsync("192.168.1.100", CancellationToken.None);
        _sut.IsConnected.Should().BeTrue();

        // Act
        var result = await _sut.DisconnectAsync(CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(DeviceStatusType.Disconnected);
        _sut.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectAsync_ValidIp_StatusChangesObservableEmits()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeConnectedResult()));

        var emittedStatuses = new List<DeviceStatus>();
        using var subscription = _sut.StatusChanges.Subscribe(s => emittedStatuses.Add(s));

        // Act
        await _sut.ConnectAsync("192.168.1.100", CancellationToken.None);

        // Assert: BehaviorSubject emits initial value on subscribe + the connected status
        emittedStatuses.Should().HaveCountGreaterOrEqualTo(2);
        emittedStatuses.Last().Status.Should().Be(DeviceStatusType.Connected);
        emittedStatuses.Last().IpAddress.Should().Be("192.168.1.100");
    }
}
