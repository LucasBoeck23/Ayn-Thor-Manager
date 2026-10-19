using Avalonia.Controls;
using Avalonia.Threading;
using AynThorManager.Core.Interfaces;
using AynThorManager.Desktop.Composition;
using AynThorManager.Services.Connection;
using AynThorManager.Services.Mount;
using AynThorManager.Services.Streaming;
using AynThorManager.Services.Telemetry;

namespace AynThorManager.Desktop;

public partial class MainWindow : Window
{
    private DeviceConnection _device = null!;
    private DirectConnectionService _directConnection = null!;
    private NetworkMonitor _networkMonitor = null!;
    private StreamingService _streaming = null!;
    private ReverseStreamingService _reverseStreaming = null!;
    private ThorMountService _thorMount = null!;
    private IConnectionSettingsStore _connectionSettings = null!;
    private ISecretStore _secrets = null!;

    /// <summary>Keyring key under which the hotspot Wi-Fi password is stored.</summary>
    private const string HotspotPasswordKey = "hotspot-password";

    /// <summary>Sentido ativo do streaming; a direção padrão é Thor→PC (Req 1.1).</summary>
    private StreamDirection _direction = StreamDirection.ThorToPc;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

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
        _directConnection = services.DirectConnection;
        _networkMonitor = services.NetworkMonitor;
        _streaming = services.Streaming;
        _reverseStreaming = services.ReverseStreaming;
        _thorMount = services.ThorMount;
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

        // Req 1/3: the mount status drives the "Abrir arquivos do Thor" button state.
        // ThorMountService raises StateChanged from background threads — the device's
        // StateChanged (command queue) can trigger an unmount, and the unmount itself may
        // run off the UI thread — so marshal onto the Dispatcher before touching controls.
        _thorMount.StateChanged += s => Dispatcher.UIThread.Post(() => OnMountStateChanged(s));

        // Reflect the initial action availability for the current direction, and the
        // initial mount-button state (unavailable until a Thor is connected).
        UpdateActionAvailability();
        UpdateMountAvailability();

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

        // Req 3.2: unmount whatever the app mounted so no FUSE mount is left orphaned
        // behind the closed window, then detach the device subscription via Dispose. The
        // await completes the unmount before the window tears down. Guarded so a failure
        // on shutdown can never block the close or escape this async-void handler, and
        // null-guarded for a close during startup before the backend finished wiring.
        try
        {
            if (_thorMount is not null)
            {
                await _thorMount.UnmountAsync();
                _thorMount.Dispose();
            }
        }
        catch
        {
            // Best-effort cleanup on shutdown: swallow so the window still closes.
        }
    }
}
