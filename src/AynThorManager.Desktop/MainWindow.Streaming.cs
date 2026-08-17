using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private Process? _scrcpyProcess;
    private System.Windows.Threading.DispatcherTimer? _repositionTimer;

    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_APPWINDOW = 0x00040000;
    const int WS_EX_TOOLWINDOW = 0x00000080;

    private async void BtnStream_Click(object sender, RoutedEventArgs e)
    {
        if (!_connectionService.IsConnected) { ConnMessage.Text = "Conecte primeiro"; return; }

        var serial = _connectionService.CurrentStatus.IpAddress;
        if (serial is null) return;
        if (!serial.Contains(':')) serial += ":5555";

        var (x, y, w, h) = CalculateScrcpyRect();

        var args = $"-s {serial} --keyboard=uhid --mouse=sdk --gamepad=uhid --video-codec=h264 --video-bit-rate=5M --max-fps=60 --max-size=1080 --no-audio --video-buffer=16 --no-clipboard-autosync --window-borderless --window-x={x} --window-y={y} --window-width={w} --window-height={h}";

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
            BtnStream.Visibility = Visibility.Collapsed;
            BtnStopStream.Visibility = Visibility.Visible;
            StreamStatus.Text = "Ativo";
            StreamStatus.Foreground = (Brush)FindResource("Success");

            SizeChanged += OnWindowResized;
            StateChanged += OnWindowStateChanged;
            LocationChanged += OnWindowMoved;

            _repositionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _repositionTimer.Tick += (_, _) => RepositionScrcpy();
            _repositionTimer.Start();

            await Task.Run(async () =>
            {
                for (var i = 0; i < 30; i++)
                {
                    await Task.Delay(100);
                    _scrcpyProcess.Refresh();
                    if (_scrcpyProcess.MainWindowHandle != IntPtr.Zero) break;
                }
            });

            HideScrcpyFromTaskbar();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Scrcpy não encontrado: {ex.Message}\nInstale via: winget install Genymobile.scrcpy", "Erro");
        }
    }

    private void BtnStopStream_Click(object sender, RoutedEventArgs e)
    {
        KillScrcpy();

        SizeChanged -= OnWindowResized;
        StateChanged -= OnWindowStateChanged;
        LocationChanged -= OnWindowMoved;
        _repositionTimer?.Stop();
        _repositionTimer = null;

        BtnStream.IsEnabled = true;
        BtnStream.Visibility = Visibility.Visible;
        BtnStopStream.Visibility = Visibility.Collapsed;
        StreamStatus.Text = "Inativo";
        StreamStatus.Foreground = (Brush)FindResource("TextSecondary");
    }

    private void KillScrcpy()
    {
        if (_scrcpyProcess is { HasExited: false })
        {
            _scrcpyProcess.Kill(true);
            _scrcpyProcess.Dispose();
            _scrcpyProcess = null;
        }
    }

    private void HideScrcpyFromTaskbar()
    {
        if (_scrcpyProcess?.MainWindowHandle is not { } hWnd || hWnd == IntPtr.Zero) return;
        var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        exStyle &= ~WS_EX_APPWINDOW;
        exStyle |= WS_EX_TOOLWINDOW;
        ShowWindow(hWnd, 0);
        SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
        ShowWindow(hWnd, 5);
    }

    private void OnWindowResized(object sender, SizeChangedEventArgs e) => RepositionScrcpy();
    private void OnWindowStateChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(RepositionScrcpy, System.Windows.Threading.DispatcherPriority.Render);
    private void OnWindowMoved(object? sender, EventArgs e) => RepositionScrcpy();

    private (int x, int y, int w, int h) CalculateScrcpyRect()
    {
        var panelPos = ScrcpyPanel.PointToScreen(new Point(0, 0));
        var panelW = ScrcpyPanel.ActualWidth;
        var panelH = ScrcpyPanel.ActualHeight;

        const double aspectRatio = 16.0 / 9.0;
        double fitW, fitH;

        if (panelW / panelH > aspectRatio)
        {
            fitH = panelH;
            fitW = panelH * aspectRatio;
        }
        else
        {
            fitW = panelW;
            fitH = panelW / aspectRatio;
        }

        var offsetX = (panelW - fitW) / 2;
        var offsetY = (panelH - fitH) / 2;

        return ((int)(panelPos.X + offsetX), (int)(panelPos.Y + offsetY), (int)fitW, (int)fitH);
    }

    private void RepositionScrcpy()
    {
        if (_scrcpyProcess is null || _scrcpyProcess.HasExited) return;
        var hWnd = _scrcpyProcess.MainWindowHandle;
        if (hWnd == IntPtr.Zero) return;

        if (WindowState == WindowState.Minimized)
        {
            ShowWindow(hWnd, 0);
            return;
        }

        ShowWindow(hWnd, 5);
        var (x, y, w, h) = CalculateScrcpyRect();
        MoveWindow(hWnd, x, y, w, h, true);
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
