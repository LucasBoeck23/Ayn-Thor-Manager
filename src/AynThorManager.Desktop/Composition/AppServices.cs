using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Adb;
using AynThorManager.Infrastructure.Network;
using AynThorManager.Infrastructure.Streaming;
using AynThorManager.Infrastructure.Settings;
using AynThorManager.Services.Adb;
using AynThorManager.Services.Connection;
using AynThorManager.Services.FileStorage;
using AynThorManager.Services.Streaming;
using AynThorManager.Services.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AynThorManager.Desktop.Composition;

/// <summary>
/// Composition root: builds the whole backend object graph in one place so the window
/// stays presentation-only. The window just holds the result and wires UI event
/// handlers (which must marshal onto the Dispatcher) to it.
/// </summary>
public sealed class AppServices
{
    public DeviceConnection Device { get; }
    public IFileStorageService FileService { get; }
    public DirectConnectionService DirectConnection { get; }
    public NetworkMonitor NetworkMonitor { get; }
    public StreamingService Streaming { get; }
    public ReverseStreamingService ReverseStreaming { get; }
    public IConnectionSettingsStore ConnectionSettings { get; }
    public ISecretStore Secrets { get; }

    private AppServices(
        DeviceConnection device,
        IFileStorageService fileService,
        DirectConnectionService directConnection,
        NetworkMonitor networkMonitor,
        StreamingService streaming,
        ReverseStreamingService reverseStreaming,
        IConnectionSettingsStore connectionSettings,
        ISecretStore secrets)
    {
        Device = device;
        FileService = fileService;
        DirectConnection = directConnection;
        NetworkMonitor = networkMonitor;
        Streaming = streaming;
        ReverseStreaming = reverseStreaming;
        ConnectionSettings = connectionSettings;
        Secrets = secrets;
    }

    /// <summary>
    /// Resolves adb (off the UI thread — it probes the filesystem and starts the adb
    /// server, which is slow on a cold start) and constructs the service graph.
    /// </summary>
    public static async Task<AppServices> CreateAsync()
    {
        var adbPath = await Task.Run(() => AdbLocator.ResolveAsync(AdbLocator.DefaultCandidates()));
        var options = Options.Create(new AdbOptions { AdbPath = adbPath });

        var executor = new AdbCommandExecutor(options, NullLogger<AdbCommandExecutor>.Instance);
        var queue = new CommandQueue(executor, NullLogger<CommandQueue>.Instance);
        var device = new DeviceConnection(queue, NullLogger<DeviceConnection>.Instance);

        var fileService = new FileStorageService(device, NullLogger<FileStorageService>.Instance);
        var directConnection = new DirectConnectionService(
            device, new WifiConnectionService(), NullLogger<DirectConnectionService>.Instance);
        var networkMonitor = new NetworkMonitor(device, NullLogger<NetworkMonitor>.Instance);
        var streaming = new StreamingService(
            device, new ScrcpyMirror(), networkMonitor, NullLogger<StreamingService>.Instance);

        // Reverse direction (PC → Thor): Sunshine host on the PC + Moonlight on the Thor.
        // One ReverseStreamingService owns its leaf collaborators (prerequisites/host are
        // Infrastructure leaves; MoonlightClient is a Services leaf). The SAME forward
        // StreamingService exposed as Streaming is handed to the reverse orchestrator, so
        // UI mutual exclusion (Req 7) and the backend operate on a single object.
        var moonlight = new MoonlightClient(device, NullLogger<MoonlightClient>.Instance);
        var reverseStreaming = new ReverseStreamingService(
            device,
            streaming,
            new SunshinePrerequisites(),
            moonlight,
            new SunshineHost(),
            NullLogger<ReverseStreamingService>.Instance);

        // Persisted hotspot preferences: SSID in a config file, password in the
        // system keyring (best-effort — degrades cleanly without a keyring backend).
        var connectionSettings = new ConnectionSettingsStore();
        var secrets = new SecretToolStore();

        return new AppServices(
            device, fileService, directConnection, networkMonitor, streaming, reverseStreaming,
            connectionSettings, secrets);
    }
}
