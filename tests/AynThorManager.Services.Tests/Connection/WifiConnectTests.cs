using AynThorManager.Services.Connection;
using AynThorManager.Services.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static AynThorManager.Services.Tests.Fakes.AdbOutputs;

namespace AynThorManager.Services.Tests.Connection;

/// <summary>Manual Wi-Fi connection: the user types ip or ip:port and clicks "Conectar".</summary>
public class WifiConnectTests
{
    private readonly FakeAdb _adb = new();
    private readonly List<ThorDevice?> _events = [];
    private readonly DeviceConnection _connection;

    public WifiConnectTests()
    {
        _connection = new DeviceConnection(_adb, NullLogger<DeviceConnection>.Instance, pollInterval: TimeSpan.Zero);
        _connection.StateChanged += _events.Add;
    }

    // ---- input validation: nothing must reach adb ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_address_is_rejected_without_calling_adb(string input)
    {
        var result = await _connection.ConnectAsync(input, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_ADDRESS", result.Error!.Code);
        Assert.Empty(_adb.Calls);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("192.168.1")]
    [InlineData("192.168.1.256")]
    [InlineData("192.168.01.17")]
    [InlineData("192.168.1.17:")]
    [InlineData("192.168.1.17:0")]
    [InlineData("192.168.1.17:70000")]
    [InlineData("192.168.1.17:abc")]
    [InlineData("thor.local:5555")]
    public async Task Invalid_address_is_rejected_without_calling_adb(string input)
    {
        var result = await _connection.ConnectAsync(input, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_IP_FORMAT", result.Error!.Code);
        Assert.Empty(_adb.Calls);
    }

    [Fact]
    public async Task Companion_port_7100_is_rejected_with_explanation()
    {
        var result = await _connection.ConnectAsync("192.168.1.17:7100", default);

        Assert.Equal("COMPANION_PORT", result.Error!.Code);
        Assert.Contains("Depuração sem fio", result.Error.Message);
        Assert.Empty(_adb.Calls);
    }

    [Fact]
    public async Task Address_is_trimmed()
    {
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial)));

        var result = await _connection.ConnectAsync($"  {WifiSerial}  ", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(WifiSerial, result.Value!.Serial);
    }

    [Fact]
    public async Task Ip_without_port_uses_adb_default_5555()
    {
        _adb.On("connect 192.168.1.17:5555", ConnectedTo("192.168.1.17:5555"))
            .On("devices -l", Devices(WifiLine("192.168.1.17:5555")));

        var result = await _connection.ConnectAsync(ThorIp, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("192.168.1.17:5555", result.Value!.Serial);
    }

    // ---- successful connections ----

    [Fact]
    public async Task First_connection_succeeds_and_notifies_the_ui()
    {
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial)));

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ThorDevice(WifiSerial, DeviceTransport.Wifi, "AYN Thor"), result.Value);
        Assert.Equal(result.Value, _connection.Current);
        Assert.Equal([result.Value], _events);
    }

    [Fact]
    public async Task Already_connected_endpoint_is_accepted_instead_of_blocking()
    {
        // Bug 3.2: the old service answered "Já existe uma conexão ADB ativa".
        _adb.On($"connect {WifiSerial}", AlreadyConnected(WifiSerial))
            .On("devices -l", DevicesUsbAndWifi);

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeviceTransport.Wifi, result.Value!.Transport);
    }

    [Fact]
    public async Task Connecting_twice_to_the_same_device_does_not_fire_a_second_event()
    {
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On($"connect {WifiSerial}", AlreadyConnected(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial)));

        await _connection.ConnectAsync(WifiSerial, default);
        await _connection.ConnectAsync(WifiSerial, default);

        Assert.Single(_events);
    }

    [Fact]
    public async Task Picks_the_exact_serial_when_usb_and_wifi_are_both_attached()
    {
        // Real situation from the Thor: cable plugged and wireless debugging on.
        _adb.On($"connect {WifiSerial}", AlreadyConnected(WifiSerial))
            .On("devices -l", DevicesUsbAndWifi);

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.Equal(WifiSerial, result.Value!.Serial);
        Assert.NotEqual(UsbSerial, result.Value.Serial);
    }

    [Fact]
    public async Task Waits_for_the_device_to_become_ready_after_connect()
    {
        // adb lists the endpoint as offline for a moment right after connecting.
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial, "offline")))
            .On("devices -l", Devices(WifiLine(WifiSerial)));

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _adb.CountOf("devices -l"));
    }

    // ---- failures ----

    [Fact]
    public async Task Ip_that_does_not_exist_fails_fast()
    {
        // recorded: adb connect 192.168.1.250:5555
        _adb.On("connect 192.168.1.250:5555", NoRouteToHost("192.168.1.250:5555"));

        var result = await _connection.ConnectAsync("192.168.1.250:5555", default);

        Assert.Equal("CONNECTION_FAILED", result.Error!.Code);
        Assert.Contains("No route to host", result.Error.Message);
        Assert.Equal(0, _adb.CountOf("devices -l")); // no pointless polling
    }

    [Fact]
    public async Task Wrong_port_or_wireless_debugging_off_is_refused()
    {
        // recorded: adb connect 192.168.1.17:5555 while the Thor listens on 43321
        _adb.On("connect 192.168.1.17:5555", ConnectionRefused("192.168.1.17:5555"));

        var result = await _connection.ConnectAsync("192.168.1.17:5555", default);

        Assert.Equal("CONNECTION_FAILED", result.Error!.Code);
        Assert.Contains("Connection refused", result.Error.Message);
        Assert.Equal(0, _adb.CountOf("devices -l"));
    }

    [Fact]
    public async Task Older_adb_cannot_connect_message_is_also_a_failure()
    {
        _adb.On($"connect {WifiSerial}", CannotConnect(WifiSerial));

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.Equal("CONNECTION_FAILED", result.Error!.Code);
    }

    [Fact]
    public async Task Host_that_never_answers_reports_timeout_without_polling()
    {
        // A firewalled IP makes adb hang until the executor's timeout fires.
        _adb.OnTimeout($"connect {WifiSerial}");

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.Equal("TIMEOUT", result.Error!.Code);
        Assert.Equal(0, _adb.CountOf("devices -l"));
    }

    [Fact]
    public async Task Already_connected_text_is_not_trusted_if_adb_does_not_list_the_device()
    {
        // Bug 3.1: adb prints "already connected" even for endpoints that are dead.
        _adb.On($"connect {WifiSerial}", AlreadyConnected(WifiSerial))
            .On("devices -l", DevicesEmpty);

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.Equal("CONNECTION_FAILED", result.Error!.Code);
        Assert.Null(_connection.Current);
        Assert.Equal(6, _adb.CountOf("devices -l"));
    }

    [Fact]
    public async Task Device_stuck_offline_is_a_failure()
    {
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial, "offline")));

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.Equal("CONNECTION_FAILED", result.Error!.Code);
        Assert.Null(_connection.Current);
    }

    [Fact]
    public async Task Pc_not_authorized_on_the_thor_asks_the_user_to_accept()
    {
        _adb.On($"connect {WifiSerial}", FailedToAuthenticate(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial, "unauthorized")));

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.Equal("UNAUTHORIZED", result.Error!.Code);
        Assert.Contains("tela do Thor", result.Error.Message);
        Assert.Null(_connection.Current);
    }

    [Fact]
    public async Task Queue_failure_is_reported_as_connection_failure()
    {
        _adb.OnQueueFailure($"connect {WifiSerial}");

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.Equal("CONNECTION_FAILED", result.Error!.Code);
    }

    [Fact]
    public async Task Failed_attempt_keeps_the_device_already_in_use()
    {
        // Establish a working connection first, then a failed connect to another
        // endpoint must keep the device already in use.
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial)))
            .On("connect 192.168.1.250:5555", NoRouteToHost("192.168.1.250:5555"));
        Assert.True((await _connection.ConnectAsync(WifiSerial, default)).IsSuccess);
        _events.Clear();

        var result = await _connection.ConnectAsync("192.168.1.250:5555", default);

        Assert.False(result.IsSuccess);
        Assert.Equal(WifiSerial, _connection.Current!.Serial);
        Assert.Empty(_events);
    }
}
