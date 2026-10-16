using Avalonia.Controls;
using Avalonia.Threading;
using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Adb;
using AynThorManager.Services.Adb;
using AynThorManager.Services.Connection;
using AynThorManager.Services.FileStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AynThorManager.Desktop;

public partial class MainWindow : Window
{
    private readonly DeviceConnection _device;
    private readonly IFileStorageService _fileService;
    private string _currentPath = "/storage/";

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
        var adbPath = AdbLocator.ResolveAsync(AdbLocator.DefaultCandidates()).GetAwaiter().GetResult();
        var options = Options.Create(new AdbOptions { AdbPath = adbPath });
        var executor = new AdbCommandExecutor(options, NullLogger<AdbCommandExecutor>.Instance);
        var queue = new CommandQueue(executor, NullLogger<CommandQueue>.Instance);

        _device = new DeviceConnection(queue, NullLogger<DeviceConnection>.Instance);
        _device.StateChanged += device => Dispatcher.UIThread.Post(() => OnDeviceChanged(device));
        _fileService = new FileStorageService(_device, NullLogger<FileStorageService>.Instance);

        SetupFileDragDrop();
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
