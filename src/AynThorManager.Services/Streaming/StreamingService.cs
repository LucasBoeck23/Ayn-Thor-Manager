using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using AynThorManager.Services.Telemetry;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Streaming;

public sealed class StreamingService(
    DeviceConnection device,
    IScreenMirror mirror,
    NetworkMonitor monitor,
    ILogger<StreamingService> logger)
{
    private const int JitterSamples = 5;

    private const string Codec = "h265";
    private const int BitrateMbps = 24;
    private const int MaxFps = 120;

    private const string AudioSource = "output";
    private const int AudioBufferMs = 50;

    public bool IsRunning => mirror.IsRunning;

    public event Action? Exited
    {
        add => mirror.Exited += value;
        remove => mirror.Exited -= value;
    }

    public async Task<Result> StartAsync(CancellationToken ct)
    {
        var deviceSerial = device.Current?.Serial;
        if (deviceSerial is null)
            return Result.Failure(new Error("DEVICE_NOT_CONNECTED", "Conecte primeiro."));

        if (mirror.IsRunning)
            return Result.Success();

        var buffer = await PickAdaptiveBufferAsync(ct);
        var args = BuildArgs(deviceSerial, buffer);
        logger.LogInformation("Starting scrcpy: {Args}", args);

        try
        {
            mirror.Start([args]);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start scrcpy");
            return Result.Failure(new Error("SCRCPY_START_FAILED",
                $"Scrcpy não encontrado: {ex.Message}\nInstale com: sudo apt install scrcpy"));
        }
    }

    public void Stop() => mirror.Stop();

    private static string BuildArgs(string serial, int buffer) =>
        $"-s {serial} --keyboard=uhid --mouse=sdk --gamepad=uhid " +
        $"--video-codec={Codec} --video-bit-rate={BitrateMbps}M --max-fps={MaxFps} " +
        $"--video-buffer={buffer} --audio-source={AudioSource} --audio-buffer={AudioBufferMs} " +
        $"--disable-screensaver --window-title=\"AYN Thor\"";


    private async Task<int> PickAdaptiveBufferAsync(CancellationToken ct)
    {
        var jitter = await monitor.SampleJitterAsync(JitterSamples, ct);
        if (jitter is null)
            return 0;

        return jitter switch
        {
            < 8 => 0,
            < 20 => 10,
            < 40 => 25,
            _ => 40,
        };
    }
}
