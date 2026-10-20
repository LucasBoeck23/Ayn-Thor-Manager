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
    private ISecretStore _secrets = null!;

    private const string HotspotSsidKey = "hotspot-ssid";
    private const string HotspotPasswordKey = "hotspot-password";

    private StreamDirection _direction = StreamDirection.ThorToPc;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

        _ = InitializeDeviceAsync();

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
        _secrets = services.Secrets;

        _device.StateChanged += d => Dispatcher.UIThread.Post(() => OnDeviceChanged(d));
        _directConnection.Progress += msg => Dispatcher.UIThread.Post(() => ShowDirect(msg));
        _streaming.Exited += () => Dispatcher.UIThread.Post(OnScrcpyExited);
        _reverseStreaming.StateChanged += s => Dispatcher.UIThread.Post(() => OnReverseStateChanged(s));

        _thorMount.StateChanged += s => Dispatcher.UIThread.Post(() => OnMountStateChanged(s));

        UpdateActionAvailability();
        UpdateMountAvailability();

        await PrefillSavedConnectionAsync();
    }

    private async Task PrefillSavedConnectionAsync()
    {
        var ssid = await _secrets.GetAsync(HotspotSsidKey);
        if (!string.IsNullOrWhiteSpace(ssid))
            TxtThorSsid.Text = ssid;

        var password = await _secrets.GetAsync(HotspotPasswordKey);
        if (!string.IsNullOrEmpty(password))
            TxtThorPassword.Text = password;
    }

    private async Task SaveConnectionAsync(string ssid, string password)
    {
        if (string.IsNullOrWhiteSpace(ssid))
            await _secrets.DeleteAsync(HotspotSsidKey);
        else
            await _secrets.SetAsync(HotspotSsidKey, ssid);

        if (string.IsNullOrEmpty(password))
            await _secrets.DeleteAsync(HotspotPasswordKey);
        else
            await _secrets.SetAsync(HotspotPasswordKey, password);
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            await StopStreamingAsync();
        }
        catch
        {
        }

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
        }
    }
}
