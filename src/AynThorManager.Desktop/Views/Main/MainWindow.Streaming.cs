using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Single action button (Req 1.2): dispatches to the service for the selected
    /// direction. Thor→PC runs the existing scrcpy flow; PC→Thor runs the reverse flow.
    /// </summary>
    private async void BtnStream_Click(object? sender, RoutedEventArgs e)
    {
        if (_direction == StreamDirection.PcToThor)
        {
            await StartReverseAsync();
            return;
        }

        await StartForwardAsync();
    }

    private async Task StartForwardAsync()
    {
        if (_streaming.IsRunning) return;

        // Req 7.2: starting one direction ends the other. The forward service
        // (StreamingService) does NOT stop the reverse on its own — only the reverse
        // orchestrator stops scrcpy — so the UI must end the reverse here to keep the
        // two directions from running at once and the UI state coherent.
        if (_reverseStreaming.IsRunning)
            _reverseStreaming.Stop();

        BtnStream.IsEnabled = false;
        StreamStatus.Text = "Medindo a rede...";
        StreamStatus.Foreground = SecondaryBrush;

        var result = await _streaming.StartAsync(default);
        BtnStream.IsEnabled = true;

        if (!result.IsSuccess)
        {
            ConnMessage.Text = result.Error!.Message;
            StreamStatus.Text = "Inativo";
            StreamStatus.Foreground = SecondaryBrush;
            return;
        }

        BtnStream.IsVisible = false;
        BtnStopStream.IsVisible = true;
        StreamStatus.Text = "Ativo (janela do scrcpy)";
        StreamStatus.Foreground = SuccessBrush;
    }

    private void OnScrcpyExited()
    {
        // An external scrcpy exit (window closed) only affects the forward direction. If
        // a reverse stream is active (exclusion means scrcpy isn't), leave it untouched
        // so a stale forward exit can't tear down the reverse or reset the wrong UI.
        if (_reverseStreaming.IsRunning)
            return;

        if (BtnStopStream.IsVisible)
            StopStreaming();
    }

    private void BtnStopStream_Click(object? sender, RoutedEventArgs e) => StopStreaming();

    private void StopStreaming()
    {
        // Stop whichever direction is running. The backend enforces mutual exclusion,
        // so at most one is active; stopping both here is safe and keeps the UI simple.
        //
        // Req 9.3: the window can close before InitializeDeviceAsync has finished wiring
        // the backend, in which case these fields are still null (declared `= null!`).
        // No Sunshine can have been started yet in that window, but we guard defensively
        // so closing during startup kills a started Sunshine without throwing an NRE out
        // of the async-void Closing handler. Stop() on either service is idempotent, so
        // calling it when nothing is running is safe.
        _streaming?.Stop();
        _reverseStreaming?.Stop();

        BtnStopStream.IsVisible = false;
        BtnSunshineWebUi.IsVisible = false;
        StreamStatus.Text = "Inativo";
        StreamStatus.Foreground = SecondaryBrush;

        // Back to idle: the best-effort advisory (e.g. software-encode warning) belonged
        // to the previous reverse attempt, so clear it.
        WarningBanner.IsVisible = false;
        WarningText.Text = "";

        UpdateActionAvailability();
    }

    private Task StopStreamingAsync()
    {
        StopStreaming();
        return Task.CompletedTask;
    }
}
