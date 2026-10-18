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
    private ReverseStreamingService _reverseStreaming = null!;
    private IConnectionSettingsStore _connectionSettings = null!;
    private ISecretStore _secrets = null!;
    private string _currentPath = "/storage/";

    /// <summary>Keyring key under which the hotspot Wi-Fi password is stored.</summary>
    private const string HotspotPasswordKey = "hotspot-password";

    /// <summary>Sentido ativo do streaming; a direção padrão é Thor→PC (Req 1.1).</summary>
    private StreamDirection _direction = StreamDirection.ThorToPc;

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
        _reverseStreaming = services.ReverseStreaming;
        _connectionSettings = services.ConnectionSettings;
        _secrets = services.Secrets;

        // UI event wiring: marshal backend callbacks onto the Dispatcher.
        _device.StateChanged += d => Dispatcher.UIThread.Post(() => OnDeviceChanged(d));
        _directConnection.Progress += msg => Dispatcher.UIThread.Post(() => ShowDirect(msg));
        _streaming.Exited += () => Dispatcher.UIThread.Post(OnScrcpyExited);
        // Req 9: StateChanged can fire from background threads (the Sunshine host's Exited
        // on a thread-pool thread, or device.StateChanged from the command queue), so
        // marshaling onto the Dispatcher is required — OnReverseStateChanged touches
        // controls. The service's only cross-thread event is StateChanged; the prereq and
        // start calls are awaited inside UI-thread handlers, so their continuations resume
        // on the UI thread already.
        _reverseStreaming.StateChanged += s => Dispatcher.UIThread.Post(() => OnReverseStateChanged(s));

        // Reflect the initial action availability for the current direction.
        UpdateActionAvailability();

        // Pre-fill the last-used hotspot SSID/password so the user doesn't retype them.
        // The SSID comes from the config file; the password from the system keyring.
        await PrefillSavedConnectionAsync();
    }

    /// <summary>
    /// Loads the saved SSID (config) and password (keyring) and fills the connection
    /// fields. Best-effort: anything missing just leaves the field blank.
    /// </summary>
    private async Task PrefillSavedConnectionAsync()
    {
        var settings = await _connectionSettings.LoadAsync();
        if (!string.IsNullOrWhiteSpace(settings.LastSsid))
            TxtThorSsid.Text = settings.LastSsid;

        var password = await _secrets.GetAsync(HotspotPasswordKey);
        if (!string.IsNullOrEmpty(password))
            TxtThorPassword.Text = password;
    }

    /// <summary>
    /// Remembers the hotspot SSID (config) and password (keyring) after a successful
    /// connect, so the next launch pre-fills them. Best-effort — never blocks the flow.
    /// </summary>
    private async Task SaveConnectionAsync(string ssid, string password)
    {
        await _connectionSettings.SaveAsync(new Core.Models.ConnectionSettings(ssid));
        if (string.IsNullOrEmpty(password))
            await _secrets.DeleteAsync(HotspotPasswordKey);
        else
            await _secrets.SetAsync(HotspotPasswordKey, password);
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        // Req 9.3 / 3.4: when the window closes, kill the Sunshine the app started so no
        // orphan process is left behind. StopStreaming()'s host.Stop() is synchronous
        // (Process.Kill), so the kill completes before this handler returns — closing the
        // window reliably tears the host down. This is an async-void event handler, so any
        // exception that escaped would crash the app and could leave Sunshine running;
        // guard it so a stop failure can never block the window from closing or orphan a
        // process. StopStreaming already null-guards the services for the case where the
        // window closes before the backend finished initializing.
        try
        {
            await StopStreamingAsync();
        }
        catch
        {
            // Best-effort cleanup on shutdown: swallow so the window still closes. There
            // is nothing left to recover to and the Desktop layer has no real logger.
        }
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
