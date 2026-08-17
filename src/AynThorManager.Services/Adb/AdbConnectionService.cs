using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Core.Validators;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Adb;

public sealed class AdbConnectionService(
    ICommandQueue commandQueue,
    ILogger<AdbConnectionService> logger) : IAdbConnectionManager
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private DeviceStatus _currentStatus = new(DeviceStatusType.Disconnected, null, null, DateTimeOffset.UtcNow);

    public DeviceStatus CurrentStatus { get { lock (_lock) { return _currentStatus; } } }

    public bool IsConnected => CurrentStatus.Status == DeviceStatusType.Connected;

    public async Task<Result<DeviceStatus>> ConnectAsync(string ipAddress, CancellationToken ct)
    {
        var validationResult = IpAddressValidator.Validate(ipAddress);
        if (!validationResult.IsSuccess)
            return Result<DeviceStatus>.Failure(validationResult.Error!);

        if (IsConnected)
            return Result<DeviceStatus>.Failure(new Error(
                "CONNECTION_ALREADY_ACTIVE",
                "JÃ¡ existe uma conexÃ£o ADB ativa. Desconecte antes de conectar a outro dispositivo."));

        var target = ipAddress.Contains(':') ? ipAddress : $"{ipAddress}:5555";
        var command = new AdbCommand($"connect {target}", ConnectTimeout, "connect");

        logger.LogInformation("Connecting to {Target}", target);
        var result = await commandQueue.EnqueueAsync(command, CommandPriority.Normal, ct);

        if (!result.IsSuccess)
        {
            UpdateStatus(DeviceStatusType.Disconnected, ipAddress, "Timeout ao conectar.");
            return Result<DeviceStatus>.Failure(new Error("CONNECTION_TIMEOUT", "Timeout ao conectar."));
        }

        var output = result.Value!.StandardOutput.ToLowerInvariant();

        if (output.Contains("unauthorized"))
        {
            UpdateStatus(DeviceStatusType.Unauthorized, ipAddress, "Dispositivo nÃ£o autorizado.");
            return Result<DeviceStatus>.Success(CurrentStatus);
        }

        if (output.Contains("connected"))
        {
            UpdateStatus(DeviceStatusType.Connected, ipAddress, null);
            logger.LogInformation("Connected to {Target}", target);
            return Result<DeviceStatus>.Success(CurrentStatus);
        }

        UpdateStatus(DeviceStatusType.Disconnected, ipAddress, "Timeout ao conectar.");
        return Result<DeviceStatus>.Failure(new Error("CONNECTION_TIMEOUT", "Timeout ao conectar."));
    }

    public async Task<Result<DeviceStatus>> DisconnectAsync(CancellationToken ct)
    {
        var command = new AdbCommand("disconnect", DisconnectTimeout, "disconnect");
        await commandQueue.EnqueueAsync(command, CommandPriority.Normal, ct);

        UpdateStatus(DeviceStatusType.Disconnected, null, null);
        logger.LogInformation("Disconnected");
        return Result<DeviceStatus>.Success(CurrentStatus);
    }

    private void UpdateStatus(DeviceStatusType type, string? ip, string? message)
    {
        lock (_lock)
        {
            _currentStatus = new DeviceStatus(type, ip, message, DateTimeOffset.UtcNow);
        }
    }
}
