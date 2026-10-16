using System.Net.Sockets;

namespace AynThorManager.Infrastructure.Network;

/// <summary>
/// Finds the (random) ADB wireless-debugging port on a known device IP by scanning
/// the port range Android uses for the connect endpoint.
///
/// Android picks a random port for wireless debugging, and the Debian adb build has
/// mDNS disabled, so discovery via <c>adb mdns</c> is unavailable. On a dedicated
/// hotspot network the device is the only host, so a quick parallel TCP scan of the
/// known range is a reliable way to locate the port.
///
/// This stage only reports which TCP ports are OPEN. Confirming a port is actually
/// ADB (not some other listener) is done by the caller via <c>adb connect</c>.
/// </summary>
public sealed class AdbPortScanner
{
    // Android's wireless-debugging connect port is observed in this range.
    // (The commonly recommended 37000–44000 sits inside it; we scan a bit wider.)
    public const int DefaultStartPort = 30000;
    public const int DefaultEndPort = 49999;

    private readonly int _maxParallelism;
    private readonly TimeSpan _connectTimeout;

    public AdbPortScanner(int maxParallelism = 512, TimeSpan? connectTimeout = null)
    {
        _maxParallelism = maxParallelism;
        _connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(300);
    }

    /// <summary>
    /// Returns the open TCP ports on <paramref name="ip"/> within the range,
    /// ordered so the most likely ADB ports (37000–44000) are tried first.
    /// </summary>
    public async Task<IReadOnlyList<int>> ScanAsync(
        string ip, int startPort = DefaultStartPort, int endPort = DefaultEndPort, CancellationToken ct = default)
    {
        var open = new System.Collections.Concurrent.ConcurrentBag<int>();
        using var throttler = new SemaphoreSlim(_maxParallelism);
        var tasks = new List<Task>();

        for (var port = startPort; port <= endPort; port++)
        {
            ct.ThrowIfCancellationRequested();
            await throttler.WaitAsync(ct);

            var p = port;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (await IsOpenAsync(ip, p, ct))
                        open.Add(p);
                }
                finally { throttler.Release(); }
            }, ct));
        }

        await Task.WhenAll(tasks);

        // Prefer the commonly-used sub-range first to cut down adb connect attempts.
        return open
            .OrderBy(p => p is >= 37000 and <= 44000 ? 0 : 1)
            .ThenBy(p => p)
            .ToList();
    }

    private async Task<bool> IsOpenAsync(string ip, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_connectTimeout);

            await client.ConnectAsync(ip, port, timeoutCts.Token);
            return client.Connected;
        }
        catch
        {
            return false; // closed, filtered, or timed out
        }
    }
}
