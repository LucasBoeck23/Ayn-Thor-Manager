using System.Diagnostics;
using System.Text.RegularExpressions;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Telemetry;

public sealed partial class NetworkMonitor(
    DeviceConnection device,
    ILogger<NetworkMonitor> logger)
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);

    public async Task<NetworkSample?> MeasureAsync(CancellationToken ct = default)
    {
        var latency = await PingAsync(ct);
        if (latency is null)
            return null;

        var (rssi, freq) = await ReadWifiAsync(ct);
        return new NetworkSample(latency.Value, rssi, freq);
    }

    public async Task<int?> SampleJitterAsync(int samples, CancellationToken ct = default)
    {
        var latencies = new List<int>(samples);
        for (var i = 0; i < samples; i++)
        {
            var latency = await PingAsync(ct);
            if (latency is not null)
                latencies.Add(latency.Value);
        }

        return latencies.Count < 2 ? null : latencies.Max() - latencies.Min();
    }

    private async Task<int?> PingAsync(CancellationToken ct)
    {
        if (!device.IsConnected)
            return null;

        var sw = Stopwatch.StartNew();
        var ping = await device.RunAsync("shell echo ok", PingTimeout, ct);
        sw.Stop();

        return ping.IsSuccess && ping.Value!.Success ? (int)sw.ElapsedMilliseconds : null;
    }

    private async Task<(int? Rssi, int? Freq)> ReadWifiAsync(CancellationToken ct)
    {
        var wifi = await device.RunAsync("shell dumpsys wifi | grep -m1 \"rssi=\"", PingTimeout, ct);
        if (!wifi.IsSuccess || string.IsNullOrWhiteSpace(wifi.Value!.StandardOutput))
            return (null, null);

        var output = wifi.Value.StandardOutput;

        int? freq = FrequencyRegex().Match(output) is { Success: true } f
            ? int.Parse(f.Groups[1].Value)
            : null;

        int? rssi = RssiRegex().Match(output) is { Success: true } r
            ? int.Parse(r.Groups[1].Value)
            : null;

        return (rssi, freq);
    }

    [GeneratedRegex(@"f=(\d+)")]
    private static partial Regex FrequencyRegex();

    [GeneratedRegex(@"rssi=(-?\d+)")]
    private static partial Regex RssiRegex();
}
