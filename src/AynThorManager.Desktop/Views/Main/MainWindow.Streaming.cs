using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private Process? _scrcpyProcess;

    private void BtnStream_Click(object? sender, RoutedEventArgs e)
    {
        var device = _device.Current;
        if (device is null) { ConnMessage.Text = "Conecte primeiro"; return; }
        if (_scrcpyProcess is { HasExited: false }) return; 

        var args = $"-s {device.Serial} --keyboard=uhid --mouse=sdk --gamepad=uhid " +
                   "--video-codec=h265 --video-bit-rate=12M --max-fps=60 " +
                   "--video-buffer=25 --no-audio --window-title=\"AYN Thor\"";

        try
        {
            _scrcpyProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "scrcpy",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (_scrcpyProcess is null) { ConnMessage.Text = "Não foi possível iniciar o scrcpy."; return; }

            _scrcpyProcess.EnableRaisingEvents = true;
            _scrcpyProcess.Exited += (_, _) => Dispatcher.UIThread.Post(OnScrcpyExited);

            BtnStream.IsVisible = false;
            BtnStopStream.IsVisible = true;
            StreamStatus.Text = "Ativo (janela do scrcpy)";
            StreamStatus.Foreground = SuccessBrush;
        }
        catch (Exception ex)
        {
            ConnMessage.Text = $"Scrcpy não encontrado: {ex.Message}\nInstale com: sudo apt install scrcpy";
            StopStreaming();
        }
    }

    private void OnScrcpyExited()
    {
        if (BtnStopStream.IsVisible)
            StopStreaming();
    }

    private void BtnStopStream_Click(object? sender, RoutedEventArgs e) => StopStreaming();

    private void StopStreaming()
    {
        var process = _scrcpyProcess;
        _scrcpyProcess = null;
        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) {  }
            finally { process.Dispose(); }
        }

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
