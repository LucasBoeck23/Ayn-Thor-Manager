using Avalonia.Controls;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private bool _applyingPreset;

    private readonly record struct StreamSettings(
        string Codec, int BitrateMbps, int MaxFps, int VideoBufferMs, int MaxSize);

    // On the Thor's dedicated 5 GHz hotspot the jitter is low, so the quality preset
    // drops the video buffer to 0 for minimum latency and raises fps to match the
    // Thor's high-refresh screen. Stability keeps a small buffer as a safety net for
    // weaker links.
    private static readonly StreamSettings QualityPreset = new("h265", 24, 120, 0, 0);
    private static readonly StreamSettings StabilityPreset = new("h265", 8, 30, 10, 1280);

    private void ShowStreamSettings(bool visible)
    {
        StreamSettingsPanel.IsVisible = visible;
        if (visible && CmbStreamPreset.SelectedIndex < 0)
            ApplyPreset(StabilityPreset, presetIndex: 1);
    }

    private void StreamPreset_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingPreset) return;

        switch (CmbStreamPreset.SelectedIndex)
        {
            case 0: ApplyPreset(QualityPreset, 0); break;
            case 1: ApplyPreset(StabilityPreset, 1); break;
            // index 2 (Personalizado) keeps the current slider values
        }
    }

    private void ApplyPreset(StreamSettings s, int presetIndex)
    {
        _applyingPreset = true;
        try
        {
            CmbStreamPreset.SelectedIndex = presetIndex;
            CmbCodec.SelectedIndex = s.Codec == "h264" ? 1 : 0;
            SldBitrate.Value = s.BitrateMbps;
            SldFps.Value = s.MaxFps;
            SldBuffer.Value = s.VideoBufferMs;
            SldMaxSize.Value = s.MaxSize;
            UpdateStreamLabels();
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    // A manual edit flips the preset selector to "Personalizado" (index 2).
    private void StreamSlider_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateStreamLabels();
        MarkCustomIfUserEdit();
    }

    private void StreamParam_Changed(object? sender, SelectionChangedEventArgs e) => MarkCustomIfUserEdit();

    private void MarkCustomIfUserEdit()
    {
        if (_applyingPreset) return;
        if (CmbStreamPreset is not null && CmbStreamPreset.SelectedIndex != 2)
            CmbStreamPreset.SelectedIndex = 2;

        if (StreamSettingsHint is not null && _scrcpyProcess is { HasExited: false })
            StreamSettingsHint.IsVisible = true;
    }

    private void UpdateStreamLabels()
    {
        if (LblBitrate is null) return; // controls not ready yet
        LblBitrate.Text = $"Bitrate: {(int)SldBitrate.Value} Mbps";
        LblFps.Text = $"FPS máximo: {(int)SldFps.Value}";
        LblBuffer.Text = $"Buffer de vídeo: {(int)SldBuffer.Value} ms";
        var maxSize = (int)SldMaxSize.Value;
        LblMaxSize.Text = $"Resolução máx (lado maior): {(maxSize == 0 ? "sem limite" : maxSize.ToString())}";
    }

    private StreamSettings CurrentStreamSettings() => new(
        Codec: CmbCodec.SelectedIndex == 1 ? "h264" : "h265",
        BitrateMbps: (int)SldBitrate.Value,
        MaxFps: (int)SldFps.Value,
        VideoBufferMs: (int)SldBuffer.Value,
        MaxSize: (int)SldMaxSize.Value);

    /// <summary>
    /// Builds the scrcpy argument string. When the user left the buffer at 0 (minimum
    /// latency), it first samples the network jitter and, if the link turns out to be
    /// unstable, raises the buffer for this session so the stream won't stutter.
    /// </summary>
    private async Task<string> BuildScrcpyArgsAsync(string serial)
    {
        var s = CurrentStreamSettings();

        var buffer = s.VideoBufferMs;
        if (buffer == 0)
            buffer = await PickAdaptiveBufferAsync();

        var args = $"-s {serial} --keyboard=uhid --mouse=sdk --gamepad=uhid " +
                   $"--video-codec={s.Codec} --video-bit-rate={s.BitrateMbps}M --max-fps={s.MaxFps} " +
                   $"--video-buffer={buffer} --no-audio --window-title=\"AYN Thor\"";

        if (s.MaxSize > 0)
            args += $" --max-size={s.MaxSize}";

        return args;
    }

    /// <summary>
    /// Samples round-trip latency a few times and derives a video buffer from the
    /// jitter (how much latency varies). A steady link keeps buffer 0 (lowest latency);
    /// a jittery link gets a buffer sized to absorb the variation and avoid stutter.
    /// </summary>
    private async Task<int> PickAdaptiveBufferAsync()
    {
        const int samples = 5;
        var latencies = new List<int>(samples);

        for (var i = 0; i < samples; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ping = await _device.RunAsync("shell echo ok", TimeSpan.FromSeconds(2), default);
            sw.Stop();
            if (ping.IsSuccess && ping.Value!.Success)
                latencies.Add((int)sw.ElapsedMilliseconds);
        }

        if (latencies.Count < 2)
            return 0; // couldn't measure; trust the user's choice (0)

        // Jitter = spread between the fastest and slowest round-trip.
        var jitter = latencies.Max() - latencies.Min();

        // Buffer only needs to cover the jitter. Keep it off on steady links.
        return jitter switch
        {
            < 8 => 0,     // very steady (dedicated 5 GHz): no buffer, lowest latency
            < 20 => 10,   // mild variation
            < 40 => 25,   // noticeable jitter
            _ => 40,      // unstable link: protect against stutter
        };
    }
}
