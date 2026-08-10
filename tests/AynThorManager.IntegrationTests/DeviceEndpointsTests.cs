using System.Net;
using System.Net.Http.Json;
using AynThorManager.Core.DTOs;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AynThorManager.IntegrationTests;

/// <summary>
/// Integration tests for the /api/device endpoints.
/// Uses WebApplicationFactory with a mocked IAdbConnectionManager to test the full HTTP pipeline.
/// </summary>
public sealed class DeviceEndpointsTests : IClassFixture<DeviceEndpointsTests.DeviceApiFactory>
{
    private readonly HttpClient _client;
    private readonly IAdbConnectionManager _mockConnectionManager;

    public DeviceEndpointsTests(DeviceApiFactory factory)
    {
        _mockConnectionManager = factory.MockConnectionManager;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Connect_WithValidIp_Returns200OkWithConnectedStatus()
    {
        // Arrange
        var ipAddress = "192.168.1.100";
        var connectedStatus = new DeviceStatus(
            DeviceStatusType.Connected,
            ipAddress,
            null,
            DateTimeOffset.UtcNow);

        _mockConnectionManager
            .ConnectAsync(ipAddress, Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Success(connectedStatus));

        // Act
        var response = await _client.PostAsJsonAsync("/api/device/connect", new ConnectRequestDto(ipAddress));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<DeviceStatusDto>();
        dto.Should().NotBeNull();
        dto!.Status.Should().Be("conectado");
        dto.IpAddress.Should().Be(ipAddress);
    }

    [Fact]
    public async Task Connect_WithInvalidIp_Returns400BadRequest()
    {
        // Arrange
        var invalidIp = "999.999.999.999";
        var error = new Error("INVALID_IP_FORMAT", "O endereço IP não está em formato IPv4 válido.");

        _mockConnectionManager
            .ConnectAsync(invalidIp, Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Failure(error));

        // Act
        var response = await _client.PostAsJsonAsync("/api/device/connect", new ConnectRequestDto(invalidIp));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(400);
    }

    [Fact]
    public async Task Connect_WhenAlreadyConnected_Returns409Conflict()
    {
        // Arrange
        var ipAddress = "192.168.1.100";
        var error = new Error("CONNECTION_ALREADY_ACTIVE", "Já existe uma conexão ADB ativa.");

        _mockConnectionManager
            .ConnectAsync(ipAddress, Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Failure(error));

        // Act
        var response = await _client.PostAsJsonAsync("/api/device/connect", new ConnectRequestDto(ipAddress));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(409);
    }

    [Fact]
    public async Task Disconnect_Returns200OkWithDisconnectedStatus()
    {
        // Arrange
        var disconnectedStatus = new DeviceStatus(
            DeviceStatusType.Disconnected,
            null,
            null,
            DateTimeOffset.UtcNow);

        _mockConnectionManager
            .DisconnectAsync(Arg.Any<CancellationToken>())
            .Returns(Result<DeviceStatus>.Success(disconnectedStatus));

        // Act
        var response = await _client.PostAsync("/api/device/disconnect", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<DeviceStatusDto>();
        dto.Should().NotBeNull();
        dto!.Status.Should().Be("desconectado");
        dto.IpAddress.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_Returns200OkWithCurrentStatus()
    {
        // Arrange
        var currentStatus = new DeviceStatus(
            DeviceStatusType.Connected,
            "192.168.1.50",
            null,
            DateTimeOffset.UtcNow);

        _mockConnectionManager.CurrentStatus.Returns(currentStatus);

        // Act
        var response = await _client.GetAsync("/api/device/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<DeviceStatusDto>();
        dto.Should().NotBeNull();
        dto!.Status.Should().Be("conectado");
        dto.IpAddress.Should().Be("192.168.1.50");
    }

    /// <summary>
    /// Custom WebApplicationFactory that replaces IAdbConnectionManager with a mock.
    /// </summary>
    public sealed class DeviceApiFactory : WebApplicationFactory<Program>
    {
        public IAdbConnectionManager MockConnectionManager { get; } = Substitute.For<IAdbConnectionManager>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing IAdbConnectionManager registrations
                var descriptor = services.FirstOrDefault(d =>
                    d.ServiceType == typeof(IAdbConnectionManager));
                if (descriptor is not null)
                    services.Remove(descriptor);

                // Remove the HeartbeatService background service that depends on real connections
                var heartbeatDescriptor = services.FirstOrDefault(d =>
                    d.ImplementationType?.Name == "HeartbeatService");
                if (heartbeatDescriptor is not null)
                    services.Remove(heartbeatDescriptor);

                // Remove the real IAdbCommandExecutor to avoid CliWrap dependency in tests
                var executorDescriptor = services.FirstOrDefault(d =>
                    d.ServiceType == typeof(IAdbCommandExecutor));
                if (executorDescriptor is not null)
                    services.Remove(executorDescriptor);

                // Remove the real ICommandQueue
                var queueDescriptor = services.FirstOrDefault(d =>
                    d.ServiceType == typeof(ICommandQueue));
                if (queueDescriptor is not null)
                    services.Remove(queueDescriptor);

                // Register mocks
                services.AddSingleton(MockConnectionManager);
                services.AddSingleton(Substitute.For<IAdbCommandExecutor>());
                services.AddSingleton(Substitute.For<ICommandQueue>());
            });
        }
    }
}
