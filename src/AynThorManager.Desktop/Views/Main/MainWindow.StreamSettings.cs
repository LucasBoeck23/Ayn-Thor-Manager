using Avalonia.Controls;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private bool _applyingPreset;

    private readonly record struct StreamSettings(
        string Codec, int BitrateMbps, int MaxFps, int VideoBufferMs, int MaxSize);

    private static readonly StreamSettings QualityPreset = new("h265", 20, 60, 40, 0);
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

    /// <summary>Builds the scrcpy argument string from the current settings.</summary>
    private string BuildScrcpyArgs(string serial)
    {
        var s = CurrentStreamSettings();
        var args = $"-s {serial} --keyboard=uhid --mouse=sdk --gamepad=uhid " +
                   $"--video-codec={s.Codec} --video-bit-rate={s.BitrateMbps}M --max-fps={s.MaxFps} " +
                   $"--video-buffer={s.VideoBufferMs} --no-audio --window-title=\"AYN Thor\"";

        if (s.MaxSize > 0)
            args += $" --max-size={s.MaxSize}";

        return args;
    }
}
