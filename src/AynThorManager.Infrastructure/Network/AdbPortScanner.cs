using System.Net.Sockets;

namespace AynThorManager.Infrastructure.Network;

public sealed class AdbPortScanner
{
    public const int DefaultStartPort = 30000;
    public const int DefaultEndPort = 49999;

    private readonly int _maxParallelism;
    private readonly TimeSpan _connectTimeout;

    public AdbPortScanner(int maxParallelism = 512, TimeSpan? connectTimeout = null)
    {
        _maxParallelism = maxParallelism;
        _connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(300);
    }

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
            return false; 
        }
    }
}
