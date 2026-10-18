using Avalonia.Threading;
using AynThorManager.Core.Models;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private DispatcherTimer? _netStatsTimer;

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
        var sample = await _networkMonitor.MeasureAsync();
        if (sample is null) return;

        ShowSample(sample);
    }

    private void ShowSample(NetworkSample sample)
    {
        var latency = sample.LatencyMs;
        TxtPing.Text = $"{latency}";
        TxtPing.Foreground = latency < 50 ? SuccessBrush : latency < 150 ? AccentBrush : DangerBrush;
        TxtBand.Text = latency < 30 ? "Otima" : latency < 80 ? "Boa" : latency < 200 ? "Media" : "Ruim";

        if (sample.FrequencyMhz is not null)
        {
            TxtFreq.Text = sample.Is5GHz ? "5GHz" : "2.4GHz";
            TxtFreq.Foreground = sample.Is5GHz ? SuccessBrush : AccentBrush;
        }

        if (sample.RssiDbm is { } rssi)
        {
            var quality = rssi > -50 ? "Excelente" : rssi > -60 ? "Bom" : rssi > -70 ? "Ok" : "Fraco";
            TxtSignal.Text = $"{quality} ({rssi}dBm)";
            TxtSignal.Foreground = rssi > -60 ? SuccessBrush : rssi > -70 ? AccentBrush : DangerBrush;
        }
    }
}
