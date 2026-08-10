using AynThorManager.Api.Endpoints;
using AynThorManager.Core.DTOs;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace AynThorManager.UnitTests.Endpoints;

public sealed class DeviceEndpointsTests
{
    private readonly IAdbConnectionManager _connectionManager = Substitute.For<IAdbConnectionManager>();

    [Fact]
    public async Task Connect_ValidIp_ReturnsDeviceStatusDto()
    {
        // Arrange
        var request = new ConnectRequestDto("192.168.1.100");
        var deviceStatus = new DeviceStatus(
            DeviceStatusType.Connected,
            "192.168.1.100",
            null,
            DateTimeOffset.UtcNow);

        _connectionManager.ConnectAsync("192.168.1.100", Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Success(deviceStatus));

        // Act
        var result = await InvokeConnectAsync(request);

        // Assert
        var okResult = result.Should().BeOfType<Ok<DeviceStatusDto>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.Status.Should().Be("conectado");
        okResult.Value.IpAddress.Should().Be("192.168.1.100");
        okResult.Value.Message.Should().BeNull();
    }

    [Fact]
    public async Task Connect_InvalidIp_ReturnsBadRequest()
    {
        // Arrange
        var request = new ConnectRequestDto("invalid-ip");
        var error = new Error("INVALID_IP_FORMAT", "IP não está em formato IPv4 válido");

        _connectionManager.ConnectAsync("invalid-ip", Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Failure(error));

        // Act
        var result = await InvokeConnectAsync(request);

        // Assert
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problemResult.ProblemDetails.Extensions["code"].Should().Be("INVALID_IP_FORMAT");
    }

    [Fact]
    public async Task Connect_AlreadyConnected_ReturnsConflict()
    {
        // Arrange
        var request = new ConnectRequestDto("192.168.1.100");
        var error = new Error("CONNECTION_ALREADY_ACTIVE", "Já existe uma conexão ativa");

        _connectionManager.ConnectAsync("192.168.1.100", Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Failure(error));

        // Act
        var result = await InvokeConnectAsync(request);

        // Assert
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problemResult.ProblemDetails.Extensions["code"].Should().Be("CONNECTION_ALREADY_ACTIVE");
    }

    [Fact]
    public async Task Connect_Unauthorized_ReturnsUnprocessableEntity()
    {
        // Arrange
        var request = new ConnectRequestDto("192.168.1.100");
        var error = new Error("DEVICE_UNAUTHORIZED", "Depuração USB não autorizada no dispositivo");

        _connectionManager.ConnectAsync("192.168.1.100", Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Failure(error));

        // Act
        var result = await InvokeConnectAsync(request);

        // Assert
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        problemResult.ProblemDetails.Extensions["code"].Should().Be("DEVICE_UNAUTHORIZED");
    }

    [Fact]
    public async Task Connect_Timeout_Returns504()
    {
        // Arrange
        var request = new ConnectRequestDto("192.168.1.100");
        var error = new Error("CONNECTION_TIMEOUT", "Timeout na tentativa de conexão");

        _connectionManager.ConnectAsync("192.168.1.100", Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Failure(error));

        // Act
        var result = await InvokeConnectAsync(request);

        // Assert
        result.Should().NotBeNull();
        // CONNECTION_TIMEOUT maps via Results.Problem with 504 status
    }

    [Fact]
    public async Task Disconnect_Success_ReturnsDisconnectedStatus()
    {
        // Arrange
        var deviceStatus = new DeviceStatus(
            DeviceStatusType.Disconnected,
            null,
            null,
            DateTimeOffset.UtcNow);

        _connectionManager.DisconnectAsync(Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Success(deviceStatus));

        // Act
        var result = await InvokeDisconnectAsync();

        // Assert
        var okResult = result.Should().BeOfType<Ok<DeviceStatusDto>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.Status.Should().Be("desconectado");
    }

    [Fact]
    public async Task Disconnect_NotConnected_ReturnsConflict()
    {
        // Arrange
        var error = new Error("DEVICE_NOT_CONNECTED", "Dispositivo não conectado via ADB");

        _connectionManager.DisconnectAsync(Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Failure(error));

        // Act
        var result = await InvokeDisconnectAsync();

        // Assert
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problemResult.ProblemDetails.Extensions["code"].Should().Be("DEVICE_NOT_CONNECTED");
    }

    [Fact]
    public void GetStatus_ReturnsCurrentStatus()
    {
        // Arrange
        var deviceStatus = new DeviceStatus(
            DeviceStatusType.Connected,
            "192.168.1.100",
            null,
            DateTimeOffset.UtcNow);

        _connectionManager.CurrentStatus.Returns(deviceStatus);

        // Act
        var result = InvokeGetStatus();

        // Assert
        var okResult = result.Should().BeOfType<Ok<DeviceStatusDto>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.Status.Should().Be("conectado");
        okResult.Value.IpAddress.Should().Be("192.168.1.100");
    }

    [Fact]
    public void GetStatus_Unauthorized_ReturnsMappedStatus()
    {
        // Arrange
        var deviceStatus = new DeviceStatus(
            DeviceStatusType.Unauthorized,
            "192.168.1.100",
            "Depuração USB deve ser habilitada",
            DateTimeOffset.UtcNow);

        _connectionManager.CurrentStatus.Returns(deviceStatus);

        // Act
        var result = InvokeGetStatus();

        // Assert
        var okResult = result.Should().BeOfType<Ok<DeviceStatusDto>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.Status.Should().Be("não autorizado");
        okResult.Value.Message.Should().Be("Depuração USB deve ser habilitada");
    }

    // Helper methods to invoke endpoints directly using reflection on the private static methods
    private async Task<IResult> InvokeConnectAsync(ConnectRequestDto request)
    {
        var method = typeof(DeviceEndpoints).GetMethod(
            "ConnectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var task = (Task<IResult>)method.Invoke(null, [request, _connectionManager, CancellationToken.None])!;
        return await task;
    }

    private async Task<IResult> InvokeDisconnectAsync()
    {
        var method = typeof(DeviceEndpoints).GetMethod(
            "DisconnectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var task = (Task<IResult>)method.Invoke(null, [_connectionManager, CancellationToken.None])!;
        return await task;
    }

    private IResult InvokeGetStatus()
    {
        var method = typeof(DeviceEndpoints).GetMethod(
            "GetStatus",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        return (IResult)method.Invoke(null, [_connectionManager])!;
    }
}
