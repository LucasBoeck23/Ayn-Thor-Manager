using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private System.Windows.Threading.DispatcherTimer? _netStatsTimer;
    private int _pingHistory;
    private int _pingCount;

    private void StartNetStats()
    {
        NetStatsPanel.Visibility = Visibility.Visible;
        _netStatsTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _netStatsTimer.Tick += async (_, _) => await UpdateNetStats();
        _netStatsTimer.Start();
        _ = UpdateNetStats();
    }

    private void StopNetStats()
    {
        _netStatsTimer?.Stop();
        _netStatsTimer = null;
        NetStatsPanel.Visibility = Visibility.Collapsed;
    }

    private async Task UpdateNetStats()
    {
        if (!_connectionService.IsConnected) return;

        var target = GetAdbTargetPrefix();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ping = await _executor.ExecuteAsync($"{target}shell echo ok", TimeSpan.FromSeconds(2), default);
        sw.Stop();

        if (ping.Success)
        {
            var latency = (int)sw.ElapsedMilliseconds;
            _pingHistory += latency;
            _pingCount++;

            TxtPing.Text = $"{latency}";
            TxtPing.Foreground = (Brush)FindResource(latency < 50 ? "Success" : latency < 150 ? "Accent" : "Danger");

            var bandEst = latency < 30 ? "Otima" : latency < 80 ? "Boa" : latency < 200 ? "Media" : "Ruim";
            TxtBand.Text = bandEst;
        }

        var wifi = await _executor.ExecuteAsync($"{target}shell dumpsys wifi | grep -m1 \"rssi=\"", TimeSpan.FromSeconds(2), default);
        if (wifi.Success && !string.IsNullOrWhiteSpace(wifi.StandardOutput))
        {
            var lines = wifi.StandardOutput;

            var freqMatch = Regex.Match(lines, @"f=(\d+)");
            if (freqMatch.Success)
            {
                var freq = int.Parse(freqMatch.Groups[1].Value);
                TxtFreq.Text = freq > 5000 ? "5GHz" : "2.4GHz";
                TxtFreq.Foreground = (Brush)FindResource(freq > 5000 ? "Success" : "Accent");
            }

            var rssiMatch = Regex.Match(lines, @"rssi=(-?\d+)");
            if (rssiMatch.Success)
            {
                var rssi = int.Parse(rssiMatch.Groups[1].Value);
                var quality = rssi > -50 ? "Excelente" : rssi > -60 ? "Bom" : rssi > -70 ? "Ok" : "Fraco";
                TxtSignal.Text = $"{quality} ({rssi}dBm)";
                TxtSignal.Foreground = (Brush)FindResource(rssi > -60 ? "Success" : rssi > -70 ? "Accent" : "Danger");
            }
        }
    }
}
