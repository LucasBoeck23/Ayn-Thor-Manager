using AynThorManager.Services.Connection;
using AynThorManager.Services.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static AynThorManager.Services.Tests.Fakes.AdbOutputs;

namespace AynThorManager.Services.Tests.Connection;

/// <summary>What happens while connected over Wi-Fi: commands, drops and disconnect.</summary>
public class WifiSessionTests
{
    private readonly FakeAdb _adb = new();
    private readonly List<ThorDevice?> _events = [];
    private readonly DeviceConnection _connection;

    public WifiSessionTests()
    {
        _connection = new DeviceConnection(_adb, NullLogger<DeviceConnection>.Instance, pollInterval: TimeSpan.Zero);
        _connection.StateChanged += _events.Add;
    }

    private async Task ConnectWifi()
    {
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial)));
        Assert.True((await _connection.ConnectAsync(WifiSerial, default)).IsSuccess);
        _events.Clear();
    }

    [Fact]
    public async Task Commands_without_a_connection_never_reach_adb()
    {
        var result = await _connection.RunAsync("shell echo ok", TimeSpan.FromSeconds(1), default);

        Assert.Equal("DEVICE_NOT_CONNECTED", result.Error!.Code);
        Assert.Empty(_adb.Calls);
    }

    [Fact]
    public async Task Every_command_targets_the_exact_serial_including_the_random_port()
    {
        // Bug 5.2/6.1: commands went without -s or with a forced :5555.
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} shell echo ok", "ok\n");

        var result = await _connection.RunAsync("shell echo ok", TimeSpan.FromSeconds(1), default);

        Assert.True(result.Value!.Success);
        Assert.Equal($"-s {WifiSerial} shell echo ok", _adb.Calls[^1]);
    }

    [Theory]
    [InlineData("device-not-found")]
    [InlineData("device-offline")]
    [InlineData("closed")]
    public async Task Wifi_drop_clears_the_connection_and_notifies_the_ui(string kind)
    {
        await ConnectWifi();
        var stderr = kind switch
        {
            "device-not-found" => DeviceNotFound(WifiSerial), // recorded
            "device-offline" => DeviceOffline,
            _ => Closed
        };
        _adb.On($"-s {WifiSerial} shell echo ok", stderr: stderr, exitCode: 1);

        var result = await _connection.RunAsync("shell echo ok", TimeSpan.FromSeconds(1), default);

        Assert.Equal("DEVICE_LOST", result.Error!.Code);
        Assert.Null(_connection.Current);
        Assert.Equal([null], _events);
    }

    [Fact]
    public async Task After_a_drop_new_commands_are_refused_until_reconnecting()
    {
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} shell echo ok", stderr: DeviceNotFound(WifiSerial), exitCode: 1);
        await _connection.RunAsync("shell echo ok", TimeSpan.FromSeconds(1), default);
        var callsAfterDrop = _adb.Calls.Count;

        var result = await _connection.RunAsync("shell echo ok", TimeSpan.FromSeconds(1), default);

        Assert.Equal("DEVICE_NOT_CONNECTED", result.Error!.Code);
        Assert.Equal(callsAfterDrop, _adb.Calls.Count);
    }

    [Fact]
    public async Task Shell_command_errors_do_not_count_as_a_drop()
    {
        // recorded: adb -s 192.168.1.17:43321 shell ls /naoexiste
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} shell ls /naoexiste", stderr: LsNoSuchFile, exitCode: 1);

        var result = await _connection.RunAsync("shell ls /naoexiste", TimeSpan.FromSeconds(1), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Success);
        Assert.NotNull(_connection.Current);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task A_single_slow_command_does_not_drop_the_connection()
    {
        await ConnectWifi();
        _adb.OnTimeout($"-s {WifiSerial} shell echo ok");

        var result = await _connection.RunAsync("shell echo ok", TimeSpan.FromSeconds(1), default);

        Assert.False(result.Value!.Success);
        Assert.NotNull(_connection.Current);
    }

    [Fact]
    public async Task Disconnect_closes_the_wifi_session_on_adb()
    {
        await ConnectWifi();
        _adb.On($"disconnect {WifiSerial}", $"disconnected {WifiSerial}\n");

        await _connection.DisconnectAsync(default);

        Assert.Null(_connection.Current);
        Assert.Contains($"disconnect {WifiSerial}", _adb.Calls);
        Assert.Equal([null], _events);
    }

    [Fact]
    public async Task Disconnect_without_a_connection_does_nothing()
    {
        await _connection.DisconnectAsync(default);

        Assert.Empty(_adb.Calls);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task Disconnecting_usb_does_not_send_adb_disconnect()
    {
        _adb.On("devices -l", Devices(UsbLine()));
        await _connection.AutoConnectAsync(default);

        await _connection.DisconnectAsync(default);

        Assert.Null(_connection.Current);
        Assert.DoesNotContain(_adb.Calls, c => c.StartsWith("disconnect"));
    }

    [Fact]
    public async Task Reconnecting_after_a_drop_works()
    {
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} shell echo ok", stderr: DeviceNotFound(WifiSerial), exitCode: 1);
        await _connection.RunAsync("shell echo ok", TimeSpan.FromSeconds(1), default);

        var result = await _connection.ConnectAsync(WifiSerial, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(WifiSerial, _connection.Current!.Serial);
    }
}
