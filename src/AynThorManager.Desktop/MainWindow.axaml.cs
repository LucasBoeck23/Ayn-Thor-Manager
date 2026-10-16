using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using AynThorManager.Core.Interfaces;
using AynThorManager.Desktop.Embedding;
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
    private readonly IScreenEmbedder _embedder;
    private string _currentPath = "/storage/";

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

        var options = Options.Create(new AdbOptions { AdbPath = "adb" });
        var executor = new AdbCommandExecutor(options, NullLogger<AdbCommandExecutor>.Instance);
        var queue = new CommandQueue(executor, NullLogger<CommandQueue>.Instance);

        _device = new DeviceConnection(queue, NullLogger<DeviceConnection>.Instance);
        _device.StateChanged += device => Dispatcher.UIThread.Post(() => OnDeviceChanged(device));
        _fileService = new FileStorageService(_device, NullLogger<FileStorageService>.Instance);

        _embedder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Win32ScreenEmbedder()
            : new X11ScreenEmbedder();
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        KillScrcpy();
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
