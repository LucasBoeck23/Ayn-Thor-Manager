using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private Process? _scrcpyProcess;
    private nint _scrcpyWindow;
    private bool _resizeHooked;

    private async void BtnStream_Click(object? sender, RoutedEventArgs e)
    {
        if (!_connectionService.IsConnected) { ConnMessage.Text = "Conecte primeiro"; return; }

        var serial = _connectionService.CurrentStatus.IpAddress;
        if (serial is null) return;
        if (!serial.Contains(':')) serial += ":5555";

        var args = $"-s {serial} --keyboard=uhid --mouse=sdk --gamepad=uhid --video-codec=h264 --video-bit-rate=5M --max-fps=60 --max-size=1080 --no-audio --video-buffer=16 --no-clipboard-autosync --window-borderless";

        try
        {
            _scrcpyProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "scrcpy",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (_scrcpyProcess is null) return;

            BtnStream.IsEnabled = false;
            BtnStream.IsVisible = false;
            BtnStopStream.IsVisible = true;
            StreamStatus.Text = "Ativo";
            StreamStatus.Foreground = SuccessBrush;

            var hostHandle = ScrcpyPanel.HostHandle;

            _scrcpyWindow = await _embedder.WaitForWindowAsync(
                _scrcpyProcess.Id, TimeSpan.FromSeconds(10), default);

            if (_scrcpyWindow == nint.Zero)
            {
                ConnMessage.Text = "Nao foi possivel capturar a janela do scrcpy.";
                StopStreaming();
                return;
            }

            _embedder.Embed(hostHandle, _scrcpyWindow);
            ResizeScrcpyToHost();

            if (!_resizeHooked)
            {
                ScrcpyPanel.PropertyChanged += (_, args) =>
                {
                    if (args.Property == BoundsProperty) ResizeScrcpyToHost();
                };
                _resizeHooked = true;
            }
        }
        catch (Exception ex)
        {
            ConnMessage.Text = $"Scrcpy nao encontrado: {ex.Message}\nInstale via: sudo apt install scrcpy";
            StopStreaming();
        }
    }

    private void BtnStopStream_Click(object? sender, RoutedEventArgs e) => StopStreaming();

    private void StopStreaming()
    {
        KillScrcpy();

        BtnStream.IsEnabled = true;
        BtnStream.IsVisible = true;
        BtnStopStream.IsVisible = false;
        StreamStatus.Text = "Inativo";
        StreamStatus.Foreground = SecondaryBrush;
    }

    private void ResizeScrcpyToHost()
    {
        if (_scrcpyWindow == nint.Zero) return;

        var scale = (this.VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        var bounds = ScrcpyPanel.Bounds;
        var w = (int)(bounds.Width * scale);
        var h = (int)(bounds.Height * scale);

        Dispatcher.UIThread.Post(() => _embedder.Resize(_scrcpyWindow, w, h));
    }

    private void KillScrcpy()
    {
        _scrcpyWindow = nint.Zero;
        if (_scrcpyProcess is { HasExited: false })
        {
            _scrcpyProcess.Kill(true);
            _scrcpyProcess.Dispose();
            _scrcpyProcess = null;
        }
    }
}
