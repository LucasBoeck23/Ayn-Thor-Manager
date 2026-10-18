using Avalonia.Controls;
using Avalonia.Threading;
using AynThorManager.Core.Interfaces;
using AynThorManager.Desktop.Composition;
using AynThorManager.Services.Connection;
using AynThorManager.Services.Streaming;
using AynThorManager.Services.Telemetry;

namespace AynThorManager.Desktop;

public partial class MainWindow : Window
{
    private DeviceConnection _device = null!;
    private IFileStorageService _fileService = null!;
    private DirectConnectionService _directConnection = null!;
    private NetworkMonitor _networkMonitor = null!;
    private StreamingService _streaming = null!;
    private string _currentPath = "/storage/";

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

        SetupFileDragDrop();

        // Resolving which adb to use probes the filesystem and runs "adb mdns check",
        // which starts the adb server (several seconds on a cold start). Doing that in
        // the constructor used to block the UI thread, so the window only appeared once
        // adb was ready. Build the backend off the UI thread so the window shows at once.
        _ = InitializeDeviceAsync();

        // Make sure the window comes to the front on launch (some compositors open
        // it unfocused/behind otherwise).
        Opened += (_, _) => Activate();
    }

    private async Task InitializeDeviceAsync()
    {
        var services = await AppServices.CreateAsync();

        _device = services.Device;
        _fileService = services.FileService;
        _directConnection = services.DirectConnection;
        _networkMonitor = services.NetworkMonitor;
        _streaming = services.Streaming;

        // UI event wiring: marshal backend callbacks onto the Dispatcher.
        _device.StateChanged += d => Dispatcher.UIThread.Post(() => OnDeviceChanged(d));
        _directConnection.Progress += msg => Dispatcher.UIThread.Post(() => ShowDirect(msg));
        _streaming.Exited += () => Dispatcher.UIThread.Post(OnScrcpyExited);
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        await StopStreamingAsync();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "-";
        string[] units = ["B", "KB", "MB", "GB"];
        var i = (int)Math.Floor(Math.Log(bytes, 1024));
        return $"{bytes / Math.Pow(1024, i):F1} {units[i]}";
    }

    private static string FormatDate(DateTimeOffset when) =>
        when == DateTimeOffset.MinValue ? "" : when.ToLocalTime().ToString("dd/MM/yy HH:mm");
}

public class FileItem
{
    public string Icon { get; set; } = "";
    public string Name { get; set; } = "";
    public string Size { get; set; } = "";
    public string Modified { get; set; } = "";
    public bool IsDirectory { get; set; }
    public string FullPath { get; set; } = "";

    public static string IconFor(string name, bool isDirectory)
    {
        if (isDirectory) return "📁";

        var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".apk" or ".xapk" => "📦",
            ".zip" or ".rar" or ".7z" or ".gz" or ".tar" => "🗂️",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" => "🖼️",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "🎬",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "🎵",
            ".pdf" => "📕",
            ".txt" or ".md" or ".log" or ".json" or ".xml" or ".ini" or ".cfg" => "📄",
            ".iso" or ".bin" or ".img" => "💿",
            ".sav" or ".srm" or ".state" => "🎮",
            _ => "📄",
        };
    }
}
