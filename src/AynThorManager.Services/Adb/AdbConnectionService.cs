using System.Reactive.Subjects;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Core.Validators;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Adb;

/// <summary>
/// Manages the ADB connection lifecycle: connect, disconnect, status tracking,
/// and observable status changes for real-time WebSocket notifications.
/// </summary>
public sealed class AdbConnectionService(
    ICommandQueue commandQueue,
    ILogger<AdbConnectionService> logger) : IAdbConnectionManager, IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(5);

    private readonly BehaviorSubject<DeviceStatus> _statusSubject = new(
        new DeviceStatus(DeviceStatusType.Disconnected, null, null, DateTimeOffset.UtcNow));

    private readonly object _lock = new();

    /// <inheritdoc />
    public DeviceStatus CurrentStatus => _statusSubject.Value;

    /// <inheritdoc />
    public IObservable<DeviceStatus> StatusChanges => _statusSubject;

    /// <inheritdoc />
    public bool IsConnected => CurrentStatus.Status == DeviceStatusType.Connected;

    /// <inheritdoc />
    public async Task<Result<DeviceStatus>> ConnectAsync(string ipAddress, CancellationToken ct)
    {
        // Validate IP format
        var validationResult = IpAddressValidator.Validate(ipAddress);
        if (!validationResult.IsSuccess)
        {
            logger.LogWarning("Connection rejected: invalid IP format '{IpAddress}'", ipAddress);
            return Result<DeviceStatus>.Failure(validationResult.Error!);
        }

        // Check if already connected
        if (IsConnected)
        {
            logger.LogWarning("Connection rejected: a connection is already active to {IpAddress}", CurrentStatus.IpAddress);
            return Result<DeviceStatus>.Failure(new Error(
                "CONNECTION_ALREADY_ACTIVE",
                "Já existe uma conexão ADB ativa. Desconecte antes de conectar a outro dispositivo."));
        }

        // Execute adb connect (supports ip:port format or just ip with default port 5555)
        var target = ipAddress.Contains(':') ? ipAddress : $"{ipAddress}:5555";
        var command = new AdbCommand(
            Arguments: $"connect {target}",
            Timeout: ConnectTimeout,
            Description: "connect");

        logger.LogInformation("Attempting ADB connection to {Target}", target);

        var result = await commandQueue.EnqueueAsync(command, CommandPriority.Normal, ct);

        if (!result.IsSuccess)
        {
            // Command execution failed (timeout, cancelled, etc.)
            var timeoutStatus = CreateStatus(DeviceStatusType.Disconnected, ipAddress,
                "Timeout ao conectar: dispositivo não encontrado no endereço informado.");
            UpdateStatus(timeoutStatus);

            logger.LogWarning("Connection to {IpAddress} failed: {Error}", ipAddress, result.Error!.Code);

            return Result<DeviceStatus>.Failure(new Error(
                "CONNECTION_TIMEOUT",
                "Timeout ao conectar: dispositivo não encontrado no endereço informado."));
        }

        var output = result.Value!.StandardOutput.ToLowerInvariant();

        // Check for unauthorized
        if (output.Contains("unauthorized"))
        {
            var unauthorizedStatus = CreateStatus(DeviceStatusType.Unauthorized, ipAddress,
                "Dispositivo não autorizado. Habilite a depuração USB e aceite a conexão no dispositivo.");
            UpdateStatus(unauthorizedStatus);

            logger.LogWarning("Device at {IpAddress} is unauthorized", ipAddress);

            return Result<DeviceStatus>.Success(unauthorizedStatus);
        }

        // Check for successful connection
        if (output.Contains("connected"))
        {
            var connectedStatus = CreateStatus(DeviceStatusType.Connected, ipAddress, null);
            UpdateStatus(connectedStatus);

            logger.LogInformation("Successfully connected to {IpAddress}:5555", ipAddress);

            return Result<DeviceStatus>.Success(connectedStatus);
        }

        // Unrecognized output — treat as timeout/failure
        var failedStatus = CreateStatus(DeviceStatusType.Disconnected, ipAddress,
            "Timeout ao conectar: dispositivo não encontrado no endereço informado.");
        UpdateStatus(failedStatus);

        logger.LogWarning("Connection to {IpAddress} returned unrecognized output: {Output}",
            ipAddress, result.Value.StandardOutput);

        return Result<DeviceStatus>.Failure(new Error(
            "CONNECTION_TIMEOUT",
            "Timeout ao conectar: dispositivo não encontrado no endereço informado."));
    }

    /// <inheritdoc />
    public async Task<Result<DeviceStatus>> DisconnectAsync(CancellationToken ct)
    {
        var command = new AdbCommand(
            Arguments: "disconnect",
            Timeout: DisconnectTimeout,
            Description: "disconnect");

        logger.LogInformation("Disconnecting ADB session");

        await commandQueue.EnqueueAsync(command, CommandPriority.Normal, ct);

        var disconnectedStatus = CreateStatus(DeviceStatusType.Disconnected, null, null);
        UpdateStatus(disconnectedStatus);

        logger.LogInformation("ADB session disconnected");

        return Result<DeviceStatus>.Success(disconnectedStatus);
    }

    /// <summary>
    /// Marks the device as disconnected. Called by the HeartbeatService
    /// when consecutive heartbeat failures are detected.
    /// </summary>
    public void MarkDisconnected(string? message = null)
    {
        var status = CreateStatus(DeviceStatusType.Disconnected, CurrentStatus.IpAddress, message);
        UpdateStatus(status);

        logger.LogWarning("Device marked as disconnected: {Message}", message ?? "heartbeat failure");
    }

    public void Dispose()
    {
        _statusSubject.Dispose();
    }

    private void UpdateStatus(DeviceStatus newStatus)
    {
        lock (_lock)
        {
            _statusSubject.OnNext(newStatus);
        }
    }

    private static DeviceStatus CreateStatus(DeviceStatusType type, string? ipAddress, string? message) =>
        new(type, ipAddress, message, DateTimeOffset.UtcNow);
}
