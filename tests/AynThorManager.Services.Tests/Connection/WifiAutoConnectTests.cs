using AynThorManager.Services.Connection;
using AynThorManager.Services.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static AynThorManager.Services.Tests.Fakes.AdbOutputs;

namespace AynThorManager.Services.Tests.Connection;

/// <summary>"Buscar" when the Thor is reachable over Wi-Fi.</summary>
public class WifiAutoConnectTests
{
    private readonly FakeAdb _adb = new();
    private readonly DeviceConnection _connection;

    public WifiAutoConnectTests() =>
        _connection = new DeviceConnection(_adb, NullLogger<DeviceConnection>.Instance, pollInterval: TimeSpan.Zero);

    [Fact]
    public async Task Uses_a_wifi_device_adb_already_knows_without_reconnecting()
    {
        _adb.On("devices -l", Devices(WifiLine(WifiSerial)));

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal(new ThorDevice(WifiSerial, DeviceTransport.Wifi, "AYN Thor"), result.Value);
        Assert.Equal(["devices -l"], _adb.Calls);
    }

    [Fact]
    public async Task Prefers_the_cable_when_usb_and_wifi_are_both_available()
    {
        _adb.On("devices -l", DevicesUsbAndWifi);

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal(UsbSerial, result.Value!.Serial);
    }

    [Fact]
    public async Task Prefers_the_thor_over_another_android_on_the_network()
    {
        _adb.On("devices -l", Devices(
            WifiLine("192.168.1.30:5555", model: "Pixel_7"),
            WifiLine(WifiSerial)));

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal(WifiSerial, result.Value!.Serial);
    }

    [Fact]
    public async Task Uses_another_android_when_no_thor_is_present()
    {
        _adb.On("devices -l", Devices(WifiLine("192.168.1.30:5555", model: "Pixel_7")));

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal("Pixel 7", result.Value!.Model);
    }

    [Fact]
    public async Task Ignores_the_daemon_startup_banner()
    {
        _adb.On("devices -l", DaemonStarting + Devices(WifiLine(WifiSerial)));

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal(WifiSerial, result.Value!.Serial);
    }

    [Fact]
    public async Task Skips_an_offline_wifi_device_and_uses_a_ready_one()
    {
        _adb.On("devices -l", Devices(
            WifiLine("192.168.1.17:40000", "offline"),
            WifiLine(WifiSerial)));

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal(WifiSerial, result.Value!.Serial);
    }

    // ---- discovery through mDNS (wireless debugging announcement) ----

    [Fact]
    public async Task Connects_to_a_thor_announced_by_mdns()
    {
        _adb.On("devices -l", DevicesEmpty)
            .On("devices -l", Devices(WifiLine(WifiSerial)))
            .On("mdns services", Mdns(MdnsConnect(WifiSerial)))
            .On($"connect {WifiSerial}", ConnectedTo(WifiSerial));

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal(WifiSerial, result.Value!.Serial);
        Assert.Contains($"connect {WifiSerial}", _adb.Calls);
    }

    [Fact]
    public async Task Ignores_pairing_announcements()
    {
        // _adb-tls-pairing is the "pair with code" screen, not a connectable endpoint.
        _adb.On("devices -l", DevicesEmpty)
            .On("mdns services", Mdns(MdnsPairing("192.168.1.17:37000")));

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal("NO_DEVICE", result.Error!.Code);
        Assert.DoesNotContain(_adb.Calls, c => c.StartsWith("connect"));
    }

    [Fact]
    public async Task Tries_the_next_mdns_endpoint_when_one_fails()
    {
        _adb.On("devices -l", DevicesEmpty)
            .On("devices -l", Devices(WifiLine(WifiSerial)))
            .On("mdns services", Mdns(MdnsConnect("192.168.1.99:41000", "old"), MdnsConnect(WifiSerial)))
            .On("connect 192.168.1.99:41000", NoRouteToHost("192.168.1.99:41000"))
            .On($"connect {WifiSerial}", ConnectedTo(WifiSerial));

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal(WifiSerial, result.Value!.Serial);
    }

    [Fact]
    public async Task Reports_unauthorized_when_the_mdns_device_rejects_the_pc()
    {
        _adb.On("devices -l", DevicesEmpty)
            .On("devices -l", Devices(WifiLine(WifiSerial, "unauthorized")))
            .On("mdns services", Mdns(MdnsConnect(WifiSerial)))
            .On($"connect {WifiSerial}", FailedToAuthenticate(WifiSerial));

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal("UNAUTHORIZED", result.Error!.Code);
    }

    [Fact]
    public async Task Nothing_announced_reports_no_device()
    {
        // recorded: adb mdns services with no announcement
        _adb.On("devices -l", DevicesEmpty)
            .On("mdns services", MdnsEmpty);

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal("NO_DEVICE", result.Error!.Code);
        Assert.Contains("Depuração sem fio", result.Error.Message);
        Assert.Null(_connection.Current);
    }

    [Fact]
    public async Task Mdns_not_supported_by_adb_reports_no_device()
    {
        _adb.On("devices -l", DevicesEmpty)
            .OnQueueFailure("mdns services");

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal("NO_DEVICE", result.Error!.Code);
    }

    // ---- devices adb knows but cannot use ----

    [Fact]
    public async Task Only_unauthorized_wifi_device_asks_the_user_to_accept()
    {
        _adb.On("devices -l", Devices(WifiLine(WifiSerial, "unauthorized")))
            .On("mdns services", MdnsEmpty);

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal("UNAUTHORIZED", result.Error!.Code);
    }

    [Fact]
    public async Task Only_offline_wifi_device_is_reported_as_offline()
    {
        _adb.On("devices -l", Devices(WifiLine(WifiSerial, "offline")))
            .On("mdns services", MdnsEmpty);

        var result = await _connection.AutoConnectAsync(default);

        Assert.Equal("DEVICE_OFFLINE", result.Error!.Code);
    }
}
