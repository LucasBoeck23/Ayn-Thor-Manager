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

/// <summary>
/// Composition root: builds the whole backend object graph in one place so the window
/// stays presentation-only. The window just holds the result and wires UI event
/// handlers (which must marshal onto the Dispatcher) to it.
/// </summary>
public sealed class AppServices
{
    public DeviceConnection Device { get; }
    public DirectConnectionService DirectConnection { get; }
    public NetworkMonitor NetworkMonitor { get; }
    public StreamingService Streaming { get; }
    public ReverseStreamingService ReverseStreaming { get; }
    public ThorMountService ThorMount { get; }
    public IConnectionSettingsStore ConnectionSettings { get; }
    public ISecretStore Secrets { get; }

    private AppServices(
        DeviceConnection device,
        DirectConnectionService directConnection,
        NetworkMonitor networkMonitor,
        StreamingService streaming,
        ReverseStreamingService reverseStreaming,
        ThorMountService thorMount,
        IConnectionSettingsStore connectionSettings,
        ISecretStore secrets)
    {
        Device = device;
        DirectConnection = directConnection;
        NetworkMonitor = networkMonitor;
        Streaming = streaming;
        ReverseStreaming = reverseStreaming;
        ThorMount = thorMount;
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

        // Thor filesystem mount (native FUSE→ADB, no third-party adbfs binary). The mount
        // is built from a chain of leaves owned top-down by ThorFuseMount:
        //   ActiveDeviceAdbFileGateway → FileCache → ThorFuseFilesystem → ThorFuseMount
        //
        // Composition note — binding the gateway to the active serial:
        // AdbFileGateway is bound to ONE device serial for life and demands it at
        // construction, but this whole graph is built at startup when no Thor is connected
        // yet. The serial only becomes known at mount time (DeviceConnection.Current
        // .Serial), which ThorMountService reads just before IThorMount.MountAsync(serial).
        // ThorFuseMount.MountAsync only validates that serial; it does not re-plumb the
        // gateway. So the gateway must already target the active device by the time FUSE
        // first touches it — which is only after a mount, i.e. only after a device is
        // connected. ActiveDeviceAdbFileGateway resolves the serial lazily, on the first
        // file operation, from the live DeviceConnection and builds the real
        // AdbFileGateway bound to it then. This solves the serial-binding timing entirely
        // in the composition/Infrastructure layer, leaving the Core contracts and the
        // Services orchestrator untouched. The shared `executor` is reused so file
        // operations go through the same adb path as the rest of the app (Req 5.3).
        var fileGateway = new ActiveDeviceAdbFileGateway(executor, () => device.Current?.Serial);
        var fileCache = new FileCache(fileGateway);
        var fuseFilesystem = new ThorFuseFilesystem(fileGateway, fileCache);
        var fuseMount = new ThorFuseMount(fuseFilesystem, fileCache);

        // FuseMountPrerequisites checks only FUSE (fusermount/fusermount3) now — there is
        // no adbfs binary to look for. Unlike the old AdbfsMount, the mount (IThorMount)
        // and the prerequisites (IMountPrerequisites) are two separate objects, so each is
        // constructed independently and handed to the orchestrator. The orchestrator owns
        // the DeviceConnection subscription and is IDisposable; the window disposes it on
        // close (after unmounting) so no mount is left orphaned (Req 3.2/4.2).
        var thorMount = new ThorMountService(
            device,
            new FuseMountPrerequisites(),
            fuseMount,
            new GtkBookmark(),
            new FileManagerLauncher(),
            NullLogger<ThorMountService>.Instance);

        // Persisted hotspot preferences: SSID in a config file, password in the
        // system keyring (best-effort — degrades cleanly without a keyring backend).
        var connectionSettings = new ConnectionSettingsStore();
        var secrets = new SecretToolStore();

        return new AppServices(
            device, directConnection, networkMonitor, streaming, reverseStreaming,
            thorMount, connectionSettings, secrets);
    }
}
