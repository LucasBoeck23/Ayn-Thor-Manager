using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Adb;
using AynThorManager.Services.Adb;
using AynThorManager.Services.FileStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AynThorManager.Desktop;

public partial class MainWindow : Window
{
    private readonly IAdbCommandExecutor _executor;
    private readonly ICommandQueue _commandQueue;
    private readonly AdbConnectionService _connectionService;
    private readonly IFileStorageService _fileService;
    private Process? _scrcpyProcess;
    private string _currentPath = "/storage/";

    // Win32 API for positioning scrcpy window
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    static readonly IntPtr HWND_TOP = IntPtr.Zero;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

        // Bootstrap services (simple DI without container)
        var options = Options.Create(new AdbOptions { AdbPath = "adb" });
        _executor = new AdbCommandExecutor(options, NullLogger<AdbCommandExecutor>.Instance);
        _commandQueue = new CommandQueue(_executor, NullLogger<CommandQueue>.Instance);
        _connectionService = new AdbConnectionService(_commandQueue, NullLogger<AdbConnectionService>.Instance);
        _fileService = new FileStorageService(_commandQueue, _connectionService, NullLogger<FileStorageService>.Instance);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Kill scrcpy when app closes
        if (_scrcpyProcess is { HasExited: false })
        {
            _scrcpyProcess.Kill(true);
            _scrcpyProcess.Dispose();
            _scrcpyProcess = null;
        }
    }

    // === Connection ===
    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        BtnScan.IsEnabled = false;
        ConnMessage.Text = "Buscando...";

        try
        {
            // Check adb devices first
            var result = await _executor.ExecuteAsync("devices", TimeSpan.FromSeconds(3), default);
            if (result.Success)
            {
                foreach (var line in result.StandardOutput.Split('\n'))
                {
                    if (line.StartsWith("List") || !line.Contains('\t')) continue;
                    var parts = line.Split('\t');
                    if (parts.Length >= 2 && parts[0].Contains(':'))
                    {
                        var addr = parts[0].Trim();
                        IpInput.Text = addr;
                        await ConnectToDevice(addr);
                        return;
                    }
                }
            }

            // Try mDNS
            var mdns = await _executor.ExecuteAsync("mdns services", TimeSpan.FromSeconds(3), default);
            if (mdns.Success && !string.IsNullOrWhiteSpace(mdns.StandardOutput))
            {
                foreach (var line in mdns.StandardOutput.Split('\n'))
                {
                    if (line.StartsWith("List")) continue;
                    var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        var endpoint = parts[^1].Trim();
                        if (endpoint.Contains(':'))
                        {
                            IpInput.Text = endpoint;
                            await ConnectToDevice(endpoint);
                            return;
                        }
                    }
                }
            }

            ConnMessage.Text = "Nenhum device encontrado. Digite o IP:porta manualmente.";
        }
        catch (Exception ex) { ConnMessage.Text = $"Erro: {ex.Message}"; }
        finally { BtnScan.IsEnabled = true; }
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        var ip = IpInput.Text.Trim();
        if (string.IsNullOrEmpty(ip)) { ConnMessage.Text = "Digite o IP"; return; }
        await ConnectToDevice(ip);
    }

    private async Task ConnectToDevice(string address)
    {
        ConnMessage.Text = "Conectando...";
        var result = await _connectionService.ConnectAsync(address, default);

        if (result.IsSuccess && result.Value!.Status == DeviceStatusType.Connected)
        {
            StatusDot.Fill = (Brush)FindResource("Success");
            StatusText.Text = $"Conectado ({address})";
            ConnMessage.Text = "Conectado!";
            await LoadDirectory();
        }
        else
        {
            ConnMessage.Text = result.Error?.Message ?? result.Value?.Message ?? "Falha ao conectar";
        }
    }

    // === File Browser ===
    private async Task LoadDirectory()
    {
        PathDisplay.Text = _currentPath;
        FileList.Items.Clear();

        var result = await _fileService.ListDirectoryAsync(_currentPath, default);
        if (!result.IsSuccess) { ConnMessage.Text = result.Error!.Message; return; }

        foreach (var entry in result.Value!.Entries)
        {
            FileList.Items.Add(new FileItem
            {
                Icon = entry.Type == FileEntryType.Directory ? "📁" : "📄",
                Name = entry.Name,
                Size = entry.Type == FileEntryType.File ? FormatSize(entry.SizeBytes) : "",
                IsDirectory = entry.Type == FileEntryType.Directory,
                FullPath = _currentPath + entry.Name
            });
        }
    }

    private async void FileList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is FileItem item && item.IsDirectory)
        {
            _currentPath = item.FullPath + "/";
            await LoadDirectory();
        }
    }

    private async void BtnUp_Click(object sender, RoutedEventArgs e)
    {
        var parts = _currentPath.TrimEnd('/').Split('/');
        if (parts.Length > 2)
        {
            _currentPath = string.Join("/", parts[..^1]) + "/";
            await LoadDirectory();
        }
    }

    private async void BtnNewFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Nome da nova pasta:");
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Result)) return;

        var result = await _fileService.CreateDirectoryAsync(_currentPath, dialog.Result, default);
        if (result.IsSuccess) await LoadDirectory();
        else MessageBox.Show(result.Error!.Message, "Erro");
    }

    private System.Windows.Threading.DispatcherTimer? _repositionTimer;

    // === Streaming (Scrcpy positioned over panel with correct aspect ratio) ===
    private async void BtnStream_Click(object sender, RoutedEventArgs e)
    {
        if (!_connectionService.IsConnected) { ConnMessage.Text = "Conecte primeiro"; return; }

        var serial = _connectionService.CurrentStatus.IpAddress;
        if (serial is null) return;
        if (!serial.Contains(':')) serial += ":5555";

        // Calculate panel position and size with 16:9 aspect ratio fitting
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
            BtnStopStream.IsEnabled = true;
            StreamStatus.Text = "Ativo";
            StreamStatus.Foreground = (Brush)FindResource("Success");

            // Track resize/move to reposition scrcpy
            SizeChanged += OnWindowResized;
            StateChanged += OnWindowStateChanged;
            LocationChanged += OnWindowMoved;

            // Continuous repositioning timer (catches edge cases like snap, DPI change)
            _repositionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _repositionTimer.Tick += (_, _) => RepositionScrcpy();
            _repositionTimer.Start();

            // Wait for window handle
            await Task.Run(async () =>
            {
                for (var i = 0; i < 30; i++)
                {
                    await Task.Delay(100);
                    _scrcpyProcess.Refresh();
                    if (_scrcpyProcess.MainWindowHandle != IntPtr.Zero) break;
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Scrcpy não encontrado: {ex.Message}\nInstale via: winget install Genymobile.scrcpy", "Erro");
        }
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

        // Thor is 1920x1080 (16:9) — fit inside panel keeping aspect ratio
        const double aspectRatio = 16.0 / 9.0;
        double fitW, fitH;

        if (panelW / panelH > aspectRatio)
        {
            // Panel is wider — fit by height
            fitH = panelH;
            fitW = panelH * aspectRatio;
        }
        else
        {
            // Panel is taller — fit by width
            fitW = panelW;
            fitH = panelW / aspectRatio;
        }

        // Center within panel
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
            ShowWindow(hWnd, 0); // SW_HIDE
            return;
        }

        ShowWindow(hWnd, 5); // SW_SHOW
        var (x, y, w, h) = CalculateScrcpyRect();
        MoveWindow(hWnd, x, y, w, h, true);
        // Keep scrcpy at top of z-order (visible, not behind other windows)
        SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void BtnStopStream_Click(object sender, RoutedEventArgs e)
    {
        if (_scrcpyProcess is { HasExited: false })
        {
            _scrcpyProcess.Kill(true);
            _scrcpyProcess.Dispose();
            _scrcpyProcess = null;
        }

        SizeChanged -= OnWindowResized;
        StateChanged -= OnWindowStateChanged;
        LocationChanged -= OnWindowMoved;
        _repositionTimer?.Stop();
        _repositionTimer = null;

        BtnStream.IsEnabled = true;
        BtnStopStream.IsEnabled = false;
        StreamStatus.Text = "Inativo";
        StreamStatus.Foreground = (Brush)FindResource("TextSecondary");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "-";
        string[] units = ["B", "KB", "MB", "GB"];
        var i = (int)Math.Floor(Math.Log(bytes, 1024));
        return $"{bytes / Math.Pow(1024, i):F1} {units[i]}";
    }
}

public class FileItem
{
    public string Icon { get; set; } = "";
    public string Name { get; set; } = "";
    public string Size { get; set; } = "";
    public bool IsDirectory { get; set; }
    public string FullPath { get; set; } = "";
}
