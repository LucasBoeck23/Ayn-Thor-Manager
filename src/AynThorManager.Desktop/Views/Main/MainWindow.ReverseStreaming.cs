using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AynThorManager.Core.Models;

namespace AynThorManager.Desktop;

public sealed class PrereqItem
{
    public string Name { get; init; } = "";
    public string? Guidance { get; init; }
    public string? Url { get; init; }
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
}

public enum StreamDirection
{
    ThorToPc,
    PcToThor,
}

public partial class MainWindow
{

    private void ToggleThorToPc_Click(object? sender, RoutedEventArgs e) =>
        SelectDirection(StreamDirection.ThorToPc);

    private void TogglePcToThor_Click(object? sender, RoutedEventArgs e) =>
        SelectDirection(StreamDirection.PcToThor);

    private void SelectDirection(StreamDirection direction)
    {
        ToggleThorToPc.IsChecked = direction == StreamDirection.ThorToPc;
        TogglePcToThor.IsChecked = direction == StreamDirection.PcToThor;

        if (direction == _direction)
            return;

        _direction = direction;

        if (BtnStopStream.IsVisible)
        {
            StopStreaming();
            return;
        }

        StreamStatus.Text = "Inativo";
        StreamStatus.Foreground = SecondaryBrush;
        UpdateActionAvailability();

        if (direction == StreamDirection.PcToThor)
        {
            _ = RefreshPrerequisitesAsync();
        }
        else
        {
            SetPrereqPanelVisible(false);
            WarningBanner.IsVisible = false;
            WarningText.Text = "";
        }
    }

    private async Task StartReverseAsync()
    {
        if (_reverseStreaming.IsRunning) return;

        if (!_device.IsConnected)
        {
            StreamStatus.Text = "Conecte o Thor para usar o PC no Thor.";
            StreamStatus.Foreground = DangerBrush;
            return;
        }

        if (_streaming.IsRunning)
            _streaming.Stop();

        BtnStream.IsEnabled = false;
        StreamStatus.Text = "Verificando pré-requisitos...";
        StreamStatus.Foreground = SecondaryBrush;

        var blockingMissing = await RefreshPrerequisitesAsync();
        if (blockingMissing > 0)
        {
            BtnStream.IsEnabled = true;
            StreamStatus.Text = "Pré-requisitos pendentes";
            StreamStatus.Foreground = DangerBrush;
            return;
        }

        SetPrereqPanelVisible(false);

        var result = await _reverseStreaming.StartAsync();
        BtnStream.IsEnabled = true;

        if (!result.IsSuccess)
        {
            if (result.Error!.Code == "REVERSE_PREREQUISITES_MISSING")
            {
                await RefreshPrerequisitesAsync();
                StreamStatus.Text = "Pré-requisitos pendentes";
            }
            else
            {
                ConnMessage.Text = result.Error!.Message;
                StreamStatus.Text = "Erro";
            }
            StreamStatus.Foreground = DangerBrush;
            return;
        }

        BtnStream.IsVisible = false;
        BtnStopStream.IsVisible = true;
        BtnSunshineWebUi.IsVisible = true;
        StreamStatus.Text = "Ativo (PC no Thor via Moonlight)";
        StreamStatus.Foreground = SuccessBrush;
    }

    private readonly ObservableCollection<PrereqItem> _prereqItems = [];

    private async Task<int> RefreshPrerequisitesAsync()
    {
        if (!ReferenceEquals(PrereqList.ItemsSource, _prereqItems))
            PrereqList.ItemsSource = _prereqItems;

        var prereqs = await _reverseStreaming.CheckPrerequisitesAsync(default);
        var notOk = prereqs.Where(p => !p.Ok).ToList();

        var blocking = notOk.Where(p => p.IsBlocking).ToList();
        var warnings = notOk.Where(p => !p.IsBlocking).ToList();

        _prereqItems.Clear();
        foreach (var p in blocking)
            _prereqItems.Add(new PrereqItem { Name = p.Name, Guidance = p.Guidance, Url = p.Url });

        var show = blocking.Count > 0 && _direction == StreamDirection.PcToThor;
        SetPrereqPanelVisible(show);

        ShowWarnings(warnings);

        return blocking.Count;
    }

    private void ShowWarnings(IReadOnlyList<Prerequisite> warnings)
    {
        if (warnings.Count == 0 || _direction != StreamDirection.PcToThor)
        {
            WarningBanner.IsVisible = false;
            WarningText.Text = "";
            return;
        }

        var text = string.Join("\n\n",
            warnings.Select(p => string.IsNullOrWhiteSpace(p.Guidance) ? p.Name : p.Guidance));

        WarningText.Text = text;
        WarningBanner.IsVisible = true;
    }

    private void SetPrereqPanelVisible(bool visible)
    {
        PrereqPanel.IsVisible = visible;
        ScrcpyHint.IsVisible = !visible;
    }

    private const string SunshineWebUiUrl = "https://localhost:47990";

    private void PrereqLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string url })
            _ = OpenUrlAsync(url);
    }

    private void BtnSunshineWebUi_Click(object? sender, RoutedEventArgs e) =>
        _ = OpenUrlAsync(SunshineWebUiUrl);

    private async Task OpenUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null)
            {
                await launcher.LaunchUriAsync(uri);
                return;
            }
        }
        catch
        {
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void OnReverseStateChanged(ReverseStreamStatus status)
    {
        switch (status.State)
        {
            case ReverseStreamState.Active:
                BtnStream.IsVisible = false;
                BtnStopStream.IsVisible = true;
                BtnSunshineWebUi.IsVisible = true;
                StreamStatus.Text = "Ativo (PC no Thor via Moonlight)";
                StreamStatus.Foreground = SuccessBrush;
                break;

            case ReverseStreamState.Error:
                BtnStopStream.IsVisible = false;
                BtnSunshineWebUi.IsVisible = false;
                StreamStatus.Text = status.Reason ?? "Erro no streaming reverso.";
                StreamStatus.Foreground = DangerBrush;
                UpdateActionAvailability();
                break;

            case ReverseStreamState.Inactive:
            case ReverseStreamState.Unavailable:
            case ReverseStreamState.Checking:
            default:
                BtnStopStream.IsVisible = false;
                BtnSunshineWebUi.IsVisible = false;
                StreamStatus.Text = status.Reason ?? "Inativo";
                StreamStatus.Foreground = SecondaryBrush;
                UpdateActionAvailability();
                break;
        }
    }

    private void UpdateActionAvailability()
    {
        if (!BtnStopStream.IsVisible)
            BtnStream.IsVisible = true;

        if (_direction == StreamDirection.PcToThor)
        {
            BtnStream.Content = "Usar PC no Thor";
            var connected = _device is not null && _device.IsConnected;
            BtnStream.IsEnabled = connected;
            BtnStream.SetValue(ToolTip.TipProperty,
                connected ? null : "Conecte o Thor para usar o PC no Thor.");
        }
        else
        {
            BtnStream.Content = "Espelhar Tela";
            BtnStream.IsEnabled = true;
            BtnStream.SetValue(ToolTip.TipProperty, null);
        }
    }
}
