using System.Diagnostics;
using System.Runtime.InteropServices;
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
        var device = _device.Current;
        if (device is null) { ConnMessage.Text = "Conecte primeiro"; return; }

        var args = $"-s {device.Serial} --keyboard=uhid --mouse=sdk --gamepad=uhid --video-codec=h264 --video-bit-rate=5M --max-fps=60 --max-size=1080 --no-audio --video-buffer=16 --no-clipboard-autosync --window-borderless --window-title=AynThorMirror";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "scrcpy",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Embedding reparents scrcpy's window. On Linux that needs an X11 window,
            // so under an X11 session we force SDL to the x11 backend. Under Wayland
            // the window cannot be reparented at all, so we leave scrcpy standalone.
            if (!OperatingSystem.IsWindows())
            {
                if (IsX11Session())
                    startInfo.Environment["SDL_VIDEODRIVER"] = "x11";
            }

            _scrcpyProcess = Process.Start(startInfo);
            if (_scrcpyProcess is null) { ConnMessage.Text = "Não foi possível iniciar o scrcpy."; return; }

            _scrcpyProcess.EnableRaisingEvents = true;
            _scrcpyProcess.Exited += (_, _) => Dispatcher.UIThread.Post(OnScrcpyExited);

            BtnStream.IsEnabled = false;
            BtnStream.IsVisible = false;
            BtnStopStream.IsVisible = true;

            await TryEmbedAsync();
        }
        catch (Exception ex)
        {
            ConnMessage.Text = $"Scrcpy não encontrado: {ex.Message}\nInstale com: sudo apt install scrcpy";
            StopStreaming();
        }
    }

    private async Task TryEmbedAsync()
    {
        var canEmbed = OperatingSystem.IsWindows() || IsX11Session();
        if (!canEmbed)
        {
            // Wayland: a window from another process cannot be reparented. scrcpy
            // keeps running in its own window instead of being killed.
            StreamStatus.Text = "Ativo (janela separada — Wayland não permite embutir)";
            StreamStatus.Foreground = AccentBrush;
            return;
        }

        StreamStatus.Text = "Iniciando...";
        StreamStatus.Foreground = AccentBrush;

        var hostHandle = ScrcpyPanel.HostHandle;
        _scrcpyWindow = await _embedder.WaitForWindowAsync(_scrcpyProcess!.Id, TimeSpan.FromSeconds(10), default);

        // Could not grab the window (or scrcpy already exited): do NOT kill it, so
        // the user still gets the mirror in a separate window.
        if (_scrcpyWindow == nint.Zero || _scrcpyProcess.HasExited)
        {
            StreamStatus.Text = "Ativo (janela separada)";
            StreamStatus.Foreground = AccentBrush;
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

        StreamStatus.Text = "Ativo";
        StreamStatus.Foreground = SuccessBrush;
    }

    private static bool IsX11Session() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) &&
        !string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    private void OnScrcpyExited()
    {
        // scrcpy ended on its own (closed, device dropped, error). Reset the UI.
        if (BtnStopStream.IsVisible)
            StopStreaming();
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

        var process = _scrcpyProcess;
        _scrcpyProcess = null;
        if (process is null) return;

        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (InvalidOperationException) { /* already gone */ }
        finally { process.Dispose(); }
    }
}
