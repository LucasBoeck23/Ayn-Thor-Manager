using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private async void BtnStream_Click(object? sender, RoutedEventArgs e)
    {
        if (_streaming.IsRunning) return;

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
        if (BtnStopStream.IsVisible)
            StopStreaming();
    }

    private void BtnStopStream_Click(object? sender, RoutedEventArgs e) => StopStreaming();

    private void StopStreaming()
    {
        _streaming.Stop();
        BtnStream.IsVisible = true;
        BtnStopStream.IsVisible = false;
        StreamStatus.Text = "Inativo";
        StreamStatus.Foreground = SecondaryBrush;
    }

    private Task StopStreamingAsync()
    {
        StopStreaming();
        return Task.CompletedTask;
    }
}
