using System.Diagnostics;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Stream;

/// <summary>
/// Manages Scrcpy process for Thor→PC streaming.
/// Opens scrcpy as a borderless window with UHID input forwarding.
/// </summary>
public sealed class ScrcpyService(
    IAdbConnectionManager connectionManager,
    ILogger<ScrcpyService> logger) : IStreamService
{
    private Process? _scrcpyProcess;

    public bool IsStreaming => _scrcpyProcess is { HasExited: false };

    public Task<Result> StartAsync(CancellationToken ct)
    {
        if (IsStreaming)
            return Task.FromResult(Result.Failure(new Error("STREAM_ALREADY_ACTIVE", "Streaming já está ativo.")));

        if (!connectionManager.IsConnected)
            return Task.FromResult(Result.Failure(new Error("DEVICE_NOT_CONNECTED", "Dispositivo não está conectado.")));

        var ip = connectionManager.CurrentStatus.IpAddress;
        var serial = ip?.Contains(':') == true ? ip : $"{ip}:5555";

        // Position scrcpy centered on screen to overlap the web frame
        const int winW = 400;
        const int winH = 720;
        const int winX = 760; // (1920 - 400) / 2
        const int winY = 200; // offset from top to match web frame position

        var args = string.Join(" ", [
            $"-s {serial}",
            "--keyboard=uhid",
            "--mouse=uhid",
            "--gamepad=uhid",
            "--video-codec=h264",
            "--video-bit-rate=4M",
            "--max-fps=60",
            "--no-audio",
            "--no-clipboard-autosync",
            "--window-borderless",
            "--always-on-top",
            "--window-title=\"AYN Thor\"",
            $"--window-x={winX}",
            $"--window-y={winY}",
            $"--window-width={winW}",
            $"--window-height={winH}"
        ]);

        try
        {
            _scrcpyProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "scrcpy",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = false
            });

            if (_scrcpyProcess is null)
                return Task.FromResult(Result.Failure(new Error("STREAM_FAILED", "Falha ao iniciar o Scrcpy.")));

            logger.LogInformation("Scrcpy started: PID {Pid}", _scrcpyProcess.Id);
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Scrcpy");
            return Task.FromResult(Result.Failure(new Error("SCRCPY_NOT_FOUND",
                "Scrcpy não encontrado. Instale via: winget install Genymobile.scrcpy")));
        }
    }

    public Task<Result> StopAsync(CancellationToken ct)
    {
        if (!IsStreaming)
            return Task.FromResult(Result.Failure(new Error("STREAM_NOT_ACTIVE", "Nenhum streaming ativo.")));

        try
        {
            _scrcpyProcess!.Kill(entireProcessTree: true);
            _scrcpyProcess.Dispose();
            _scrcpyProcess = null;
            logger.LogInformation("Scrcpy stopped");
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping Scrcpy");
            return Task.FromResult(Result.Failure(new Error("STREAM_FAILED", ex.Message)));
        }
    }
}
