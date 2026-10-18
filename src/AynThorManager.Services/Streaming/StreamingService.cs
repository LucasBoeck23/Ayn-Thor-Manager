using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using AynThorManager.Services.Telemetry;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Streaming;

/// <summary>
/// Orchestrates screen mirroring with a single fixed "best" profile. The Thor serves a
/// dedicated 5 GHz hotspot, so the link is consistently fast and low-jitter — there's
/// no need to expose quality knobs. The service picks top quality (h265, high bit-rate
/// and fps, native resolution) and lets the adaptive buffer keep latency minimal while
/// still protecting against the rare bad link.
/// </summary>
public sealed class StreamingService(
    DeviceConnection device,
    IScreenMirror mirror,
    NetworkMonitor monitor,
    ILogger<StreamingService> logger)
{
    private const int JitterSamples = 5;

    // Top quality for a dedicated 5 GHz link: h265, high bit-rate, high fps, native
    // resolution. The video buffer is chosen adaptively from measured jitter.
    private const string Codec = "h265";
    private const int BitrateMbps = 24;
    private const int MaxFps = 120;

    public bool IsRunning => mirror.IsRunning;

    /// <summary>Forwarded from the underlying mirror process when it exits.</summary>
    public event Action? Exited
    {
        add => mirror.Exited += value;
        remove => mirror.Exited -= value;
    }

    /// <summary>
    /// Starts mirroring with the fixed best-quality profile. Returns a failure (not an
    /// exception) when there is no device or the mirror binary can't be launched.
    /// </summary>
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
            mirror.Start(args);
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
        $"--video-buffer={buffer} --no-audio --window-title=\"AYN Thor\"";

    /// <summary>
    /// Samples link jitter and derives a video buffer. A steady link (the usual case on
    /// the 5 GHz hotspot) keeps buffer 0 for lowest latency; a jittery link gets a
    /// buffer sized to absorb the variation and avoid stutter.
    /// </summary>
    private async Task<int> PickAdaptiveBufferAsync(CancellationToken ct)
    {
        var jitter = await monitor.SampleJitterAsync(JitterSamples, ct);
        if (jitter is null)
            return 0; // couldn't measure; assume a steady link

        return jitter switch
        {
            < 8 => 0,     // very steady (dedicated 5 GHz): no buffer, lowest latency
            < 20 => 10,   // mild variation
            < 40 => 25,   // noticeable jitter
            _ => 40,      // unstable link: protect against stutter
        };
    }
}
