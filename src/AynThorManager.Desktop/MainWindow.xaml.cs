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
    private string _currentPath = "/storage/";

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

        var options = Options.Create(new AdbOptions { AdbPath = "adb" });
        _executor = new AdbCommandExecutor(options, NullLogger<AdbCommandExecutor>.Instance);
        _commandQueue = new CommandQueue(_executor, NullLogger<CommandQueue>.Instance);
        _connectionService = new AdbConnectionService(_commandQueue, NullLogger<AdbConnectionService>.Instance);
        _fileService = new FileStorageService(_commandQueue, _connectionService, NullLogger<FileStorageService>.Instance);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        KillScrcpy();
    }

    private string GetAdbTargetPrefix()
    {
        var serial = _connectionService.CurrentStatus.IpAddress;
        if (serial is null) return "";
        if (!serial.Contains(':')) serial += ":5555";
        return $"-s {serial} ";
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
