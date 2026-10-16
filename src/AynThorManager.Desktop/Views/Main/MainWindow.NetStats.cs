using System.Text.RegularExpressions;
using Avalonia.Threading;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private DispatcherTimer? _netStatsTimer;
    private int _pingHistory;
    private int _pingCount;

    private void StartNetStats()
    {
        _netStatsTimer?.Stop(); 
        NetStatsPanel.IsVisible = true;
        _netStatsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _netStatsTimer.Tick += async (_, _) => await UpdateNetStats();
        _netStatsTimer.Start();
        _ = UpdateNetStats();
    }

    private void StopNetStats()
    {
        _netStatsTimer?.Stop();
        _netStatsTimer = null;
        NetStatsPanel.IsVisible = false;
    }

    private async Task UpdateNetStats()
    {
        if (!_device.IsConnected) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ping = await _device.RunAsync("shell echo ok", TimeSpan.FromSeconds(2), default);
        sw.Stop();

        if (ping.IsSuccess && ping.Value!.Success)
        {
            var latency = (int)sw.ElapsedMilliseconds;
            _pingHistory += latency;
            _pingCount++;

            TxtPing.Text = $"{latency}";
            TxtPing.Foreground = latency < 50 ? SuccessBrush : latency < 150 ? AccentBrush : DangerBrush;

            var bandEst = latency < 30 ? "Otima" : latency < 80 ? "Boa" : latency < 200 ? "Media" : "Ruim";
            TxtBand.Text = bandEst;
        }

        var wifi = await _device.RunAsync("shell dumpsys wifi | grep -m1 \"rssi=\"", TimeSpan.FromSeconds(2), default);
        if (wifi.IsSuccess && !string.IsNullOrWhiteSpace(wifi.Value!.StandardOutput))
        {
            var lines = wifi.Value.StandardOutput;

            var freqMatch = Regex.Match(lines, @"f=(\d+)");
            if (freqMatch.Success)
            {
                var freq = int.Parse(freqMatch.Groups[1].Value);
                TxtFreq.Text = freq > 5000 ? "5GHz" : "2.4GHz";
                TxtFreq.Foreground = freq > 5000 ? SuccessBrush : AccentBrush;
            }

            var rssiMatch = Regex.Match(lines, @"rssi=(-?\d+)");
            if (rssiMatch.Success)
            {
                var rssi = int.Parse(rssiMatch.Groups[1].Value);
                var quality = rssi > -50 ? "Excelente" : rssi > -60 ? "Bom" : rssi > -70 ? "Ok" : "Fraco";
                TxtSignal.Text = $"{quality} ({rssi}dBm)";
                TxtSignal.Foreground = rssi > -60 ? SuccessBrush : rssi > -70 ? AccentBrush : DangerBrush;
            }
        }
    }
}
