using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Adb;

/// <summary>
/// Background service that sends periodic heartbeat checks (adb get-state)
/// to detect connection loss and notify WebSocket clients on status change.
/// </summary>
public sealed class HeartbeatService(
    ICommandQueue commandQueue,
    IAdbConnectionManager connectionManager,
    IWebSocketNotifier notifier,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(3);
    private const int MaxConsecutiveFailures = 3;

    private int _consecutiveFailures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HeartbeatService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(HeartbeatInterval, stoppingToken);

            if (!connectionManager.IsConnected)
            {
                _consecutiveFailures = 0;
                continue;
            }

            var command = new AdbCommand(
                Arguments: "get-state",
                Timeout: HeartbeatTimeout,
                Description: "heartbeat");

            var result = await commandQueue.EnqueueAsync(command, CommandPriority.Critical, stoppingToken);

            if (result.IsSuccess && result.Value!.Success)
            {
                if (_consecutiveFailures > 0)
                {
                    logger.LogDebug("Heartbeat recovered after {Failures} consecutive failure(s)", _consecutiveFailures);
                }

                _consecutiveFailures = 0;
            }
            else
            {
                _consecutiveFailures++;
                logger.LogWarning(
                    "Heartbeat failure {Count}/{Max}",
                    _consecutiveFailures,
                    MaxConsecutiveFailures);

                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    logger.LogError("Device unreachable after {Max} consecutive heartbeat failures, marking as disconnected",
                        MaxConsecutiveFailures);

                    connectionManager.MarkDisconnected("Conexão perdida: dispositivo não respondeu a 3 verificações consecutivas.");

                    await notifier.SendDeviceStatusAsync(connectionManager.CurrentStatus, stoppingToken);

                    _consecutiveFailures = 0;
                }
            }
        }

        logger.LogInformation("HeartbeatService stopped");
    }
}
