using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Adb;
using AynThorManager.Infrastructure.Files;
using AynThorManager.Infrastructure.Mount;
using AynThorManager.Infrastructure.Network;
using AynThorManager.Infrastructure.Streaming;
using AynThorManager.Infrastructure.Settings;
using AynThorManager.Services.Adb;
using AynThorManager.Services.Connection;
using AynThorManager.Services.Mount;
using AynThorManager.Services.Streaming;
using AynThorManager.Services.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AynThorManager.Desktop.Composition;

public sealed class AppServices
{
    public DeviceConnection Device { get; }
    public DirectConnectionService DirectConnection { get; }
    public NetworkMonitor NetworkMonitor { get; }
    public StreamingService Streaming { get; }
    public ReverseStreamingService ReverseStreaming { get; }
    public ThorMountService ThorMount { get; }
    public ISecretStore Secrets { get; }

    private AppServices(
        DeviceConnection device,
        DirectConnectionService directConnection,
        NetworkMonitor networkMonitor,
        StreamingService streaming,
        ReverseStreamingService reverseStreaming,
        ThorMountService thorMount,
        ISecretStore secrets)
    {
        Device = device;
        DirectConnection = directConnection;
        NetworkMonitor = networkMonitor;
        Streaming = streaming;
        ReverseStreaming = reverseStreaming;
        ThorMount = thorMount;
        Secrets = secrets;
    }

    public static async Task<AppServices> CreateAsync()
    {
        var adbPath = await Task.Run(() => AdbLocator.ResolveAsync(AdbLocator.DefaultCandidates()));
        var options = Options.Create(new AdbOptions { AdbPath = adbPath });

        var executor = new AdbCommandExecutor(options, NullLogger<AdbCommandExecutor>.Instance);
        var queue = new CommandQueue(executor, NullLogger<CommandQueue>.Instance);
        var device = new DeviceConnection(queue, NullLogger<DeviceConnection>.Instance);

        var directConnection = new DirectConnectionService(
            device, new WifiConnectionService(), NullLogger<DirectConnectionService>.Instance);
        var networkMonitor = new NetworkMonitor(device, NullLogger<NetworkMonitor>.Instance);
        var streaming = new StreamingService(
            device, new ScrcpyMirror(), networkMonitor, NullLogger<StreamingService>.Instance);

        var moonlight = new MoonlightClient(device, NullLogger<MoonlightClient>.Instance);
        var reverseStreaming = new ReverseStreamingService(
            device,
            streaming,
            new SunshinePrerequisites(),
            moonlight,
            new SunshineHost(),
            NullLogger<ReverseStreamingService>.Instance);

        var fileGateway = new ActiveDeviceAdbFileGateway(executor, () => device.Current?.Serial);
        var fileCache = new FileCache(fileGateway);
        var fuseFilesystem = new ThorFuseFilesystem(fileGateway, fileCache);
        var fuseMount = new ThorFuseMount(fuseFilesystem, fileCache);

        var thorMount = new ThorMountService(
            device,
            new FuseMountPrerequisites(),
            fuseMount,
            new GtkBookmark(),
            new FileManagerLauncher(),
            NullLogger<ThorMountService>.Instance);

        var secrets = new SecretToolStore();

        return new AppServices(
            device, directConnection, networkMonitor, streaming, reverseStreaming,
            thorMount, secrets);
    }
}
