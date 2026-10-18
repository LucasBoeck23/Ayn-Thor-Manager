using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Adb;
using AynThorManager.Infrastructure.Network;
using AynThorManager.Infrastructure.Streaming;
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

    private AppServices(
        DeviceConnection device,
        IFileStorageService fileService,
        DirectConnectionService directConnection,
        NetworkMonitor networkMonitor,
        StreamingService streaming)
    {
        Device = device;
        FileService = fileService;
        DirectConnection = directConnection;
        NetworkMonitor = networkMonitor;
        Streaming = streaming;
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

        return new AppServices(device, fileService, directConnection, networkMonitor, streaming);
    }
}
