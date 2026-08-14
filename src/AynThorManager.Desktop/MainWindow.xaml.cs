using System.Diagnostics;
using System.IO;
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
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    static readonly IntPtr HWND_TOP = IntPtr.Zero;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_APPWINDOW = 0x00040000;
    const int WS_EX_TOOLWINDOW = 0x00000080;

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

            ConnMessage.Text = "Nenhum device encontrado.\nAtive 'Depuracao sem fio' no Thor e tente novamente.";
        }
        catch (Exception ex) { ConnMessage.Text = $"Erro: {ex.Message}"; }
        finally { BtnScan.IsEnabled = true; }
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        var ip = IpInput.Text.Trim();
        if (string.IsNullOrEmpty(ip)) { ConnMessage.Text = "Digite o IP"; return; }
        
        // Warn if user is trying to connect to companion port instead of ADB
        if (ip.EndsWith(":7100"))
        {
            ConnMessage.Text = "Porta 7100 e do companion, nao do ADB.\nUse 'Buscar' para encontrar a porta correta.";
            return;
        }
        
        await ConnectToDevice(ip);
    }

    private async Task ConnectToDevice(string address)
    {
        ConnMessage.Text = "Conectando...";
        var result = await _connectionService.ConnectAsync(address, default);

        if (result.IsSuccess && result.Value!.Status == DeviceStatusType.Connected)
        {
            // Verify device is actually responsive (not "offline")
            var target = address.Contains(':') ? address : $"{address}:5555";
            var verify = await _executor.ExecuteAsync($"-s {target} shell echo ok", TimeSpan.FromSeconds(3), default);
            
            if (!verify.Success || !verify.StandardOutput.Contains("ok"))
            {
                // Device connected but offline — disconnect and report
                await _executor.ExecuteAsync($"disconnect {target}", TimeSpan.FromSeconds(3), default);
                StatusDot.Fill = (Brush)FindResource("Danger");
                StatusText.Text = "Desconectado";
                ConnMessage.Text = "Device offline. Ative 'Depuracao sem fio' no Thor e tente novamente.";
                BtnInstallCompanion.Visibility = Visibility.Collapsed;
                return;
            }

            StatusDot.Fill = (Brush)FindResource("Success");
            StatusText.Text = $"Conectado ({address})";
            ConnMessage.Text = "Conectado!";
            
            StartNetStats();
            await InstallCompanionIfNeeded();
            await LoadDirectory();
        }
        else
        {
            ConnMessage.Text = result.Error?.Message ?? result.Value?.Message ?? "Falha ao conectar";
        }
    }

    private async Task InstallCompanionIfNeeded()
    {
        var target = GetAdbTargetPrefix();

        var check = await _executor.ExecuteAsync($"{target}shell pm list packages com.aynthor.link", TimeSpan.FromSeconds(5), default);
        if (check.Success && check.StandardOutput.Contains("com.aynthor.link"))
        {
            BtnInstallCompanion.Visibility = Visibility.Collapsed;
            return;
        }

        BtnInstallCompanion.Visibility = Visibility.Visible;
        ConnMessage.Text = "Companion nao encontrado no Thor.";
    }

    private bool _installing;
    private async void BtnInstallCompanion_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        _installing = true;

        var apkPath = FindApkPath();
        if (apkPath is null)
        {
            ConnMessage.Text = "APK nao encontrado no PC.";
            _installing = false;
            return;
        }

        BtnInstallCompanion.Content = "⏳ Instalando...";
        var target = GetAdbTargetPrefix();

        var install = await _executor.ExecuteAsync($"{target}install -r \"{Path.GetFullPath(apkPath)}\"", TimeSpan.FromSeconds(60), default);
        if (install.Success && install.StandardOutput.Contains("Success"))
        {
            await _executor.ExecuteAsync($"{target}shell pm grant com.aynthor.link android.permission.WRITE_SECURE_SETTINGS", TimeSpan.FromSeconds(5), default);
            await _executor.ExecuteAsync($"{target}shell am start -n com.aynthor.link/.MainActivity", TimeSpan.FromSeconds(5), default);

            BtnInstallCompanion.Visibility = Visibility.Collapsed;
            ConnMessage.Text = "Ayn Thor Link instalado!\nPode desativar 'Depuracao sem fio' no Thor.";
        }
        else
        {
            ConnMessage.Text = $"Falha: {(install.StandardError + " " + install.StandardOutput).Trim()}";
            BtnInstallCompanion.Content = "📲 Instalar Ayn Thor Link";
        }

        _installing = false;
    }

    private static string? FindApkPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "ayn-thor-link.apk"),
            Path.Combine(AppContext.BaseDirectory, "ayn-thor-link.apk"),
            @"mobile\ayn-thor-link\release\ayn-thor-link.apk",
            @"mobile\ayn-thor-link\app\build\outputs\apk\debug\app-debug.apk",
            @"mobile\ayn-thor-link\app\build\outputs\apk\release\app-release.apk",
        };

        return candidates.FirstOrDefault(File.Exists);
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
    private System.Windows.Threading.DispatcherTimer? _netStatsTimer;

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
            BtnStream.Visibility = Visibility.Collapsed;
            BtnStopStream.Visibility = Visibility.Visible;
            StreamStatus.Text = "Ativo";
            StreamStatus.Foreground = (Brush)FindResource("Success");

            // Track resize/move to reposition scrcpy
            SizeChanged += OnWindowResized;
            StateChanged += OnWindowStateChanged;
            LocationChanged += OnWindowMoved;

            // Continuous repositioning timer (catches edge cases like snap, DPI change)
            _repositionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
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

            // Hide scrcpy from taskbar
            if (_scrcpyProcess.MainWindowHandle != IntPtr.Zero)
            {
                var hWnd = _scrcpyProcess.MainWindowHandle;
                var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                exStyle &= ~WS_EX_APPWINDOW;  // Remove from taskbar
                exStyle |= WS_EX_TOOLWINDOW;  // Tool window (no taskbar icon)
                ShowWindow(hWnd, 0); // Hide briefly to apply style
                SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
                ShowWindow(hWnd, 5); // Show again
            }
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
        // Keep scrcpy always on top and in front
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void BtnStopStream_Click(object sender, RoutedEventArgs e)
    {
        if (_scrcpyProcess is { HasExited: false })
        {
            _scrcpyProcess.Kill(true);
            _scrcpyProcess.Dispose();
            _scrcpyProcess = null;
        }

        // Wake screen if it was turned off
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

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "-";
        string[] units = ["B", "KB", "MB", "GB"];
        var i = (int)Math.Floor(Math.Log(bytes, 1024));
        return $"{bytes / Math.Pow(1024, i):F1} {units[i]}";
    }

    // === Network Stats ===
    private void StartNetStats()
    {
        NetStatsPanel.Visibility = Visibility.Visible;
        _netStatsTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _netStatsTimer.Tick += async (_, _) => await UpdateNetStats();
        _netStatsTimer.Start();
        _ = UpdateNetStats(); // first update immediately
    }

    private void StopNetStats()
    {
        _netStatsTimer?.Stop();
        _netStatsTimer = null;
        NetStatsPanel.Visibility = Visibility.Collapsed;
    }

    private int _pingHistory;
    private int _pingCount;

    private async Task UpdateNetStats()
    {
        if (!_connectionService.IsConnected) return;

        var target = GetAdbTargetPrefix();

        // Ping: measure round-trip to device via adb shell echo
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ping = await _executor.ExecuteAsync($"{target}shell echo ok", TimeSpan.FromSeconds(2), default);
        sw.Stop();

        if (ping.Success)
        {
            var latency = (int)sw.ElapsedMilliseconds;
            _pingHistory += latency;
            _pingCount++;
            var avg = _pingHistory / _pingCount;

            TxtPing.Text = $"{latency}";
            TxtPing.Foreground = (Brush)FindResource(latency < 50 ? "Success" : latency < 150 ? "Accent" : "Danger");

            // Estimate usable bandwidth from latency (rough heuristic)
            var bandEst = latency < 30 ? "Otima" : latency < 80 ? "Boa" : latency < 200 ? "Media" : "Ruim";
            TxtBand.Text = bandEst;
        }

        // Wi-Fi frequency + signal (from device)
        var wifi = await _executor.ExecuteAsync($"{target}shell dumpsys wifi | grep -m1 \"rssi=\"", TimeSpan.FromSeconds(2), default);
        if (wifi.Success && !string.IsNullOrWhiteSpace(wifi.StandardOutput))
        {
            var lines = wifi.StandardOutput;
            
            // Parse frequency: f=2422 or f=5180
            var freqMatch = System.Text.RegularExpressions.Regex.Match(lines, @"f=(\d+)");
            if (freqMatch.Success)
            {
                var freq = int.Parse(freqMatch.Groups[1].Value);
                TxtFreq.Text = freq > 5000 ? "5GHz" : "2.4GHz";
                TxtFreq.Foreground = (Brush)FindResource(freq > 5000 ? "Success" : "Accent");
            }

            // Parse RSSI: rssi=-72
            var rssiMatch = System.Text.RegularExpressions.Regex.Match(lines, @"rssi=(-?\d+)");
            if (rssiMatch.Success)
            {
                var rssi = int.Parse(rssiMatch.Groups[1].Value);
                var quality = rssi > -50 ? "Excelente" : rssi > -60 ? "Bom" : rssi > -70 ? "Ok" : "Fraco";
                TxtSignal.Text = $"{quality} ({rssi}dBm)";
                TxtSignal.Foreground = (Brush)FindResource(rssi > -60 ? "Success" : rssi > -70 ? "Accent" : "Danger");
            }
        }
    }

    /// <summary>
    /// Gets the ADB -s target prefix for the connected device.
    /// Avoids "more than one device/emulator" errors.
    /// </summary>
    private string GetAdbTargetPrefix()
    {
        var serial = _connectionService.CurrentStatus.IpAddress;
        if (serial is null) return "";
        if (!serial.Contains(':')) serial += ":5555";
        return $"-s {serial} ";
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
