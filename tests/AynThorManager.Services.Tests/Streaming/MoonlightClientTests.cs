using AynThorManager.Services.Connection;
using AynThorManager.Services.Streaming;
using AynThorManager.Services.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static AynThorManager.Services.Tests.Fakes.AdbOutputs;

namespace AynThorManager.Services.Tests.Streaming;

public class MoonlightClientTests
{
    private const string PmList = "shell pm list packages com.limelight";
    private const string Launch = "shell monkey -p com.limelight -c android.intent.category.LAUNCHER 1";

    private readonly FakeAdb _adb = new();
    private readonly DeviceConnection _connection;
    private readonly MoonlightClient _moonlight;

    public MoonlightClientTests()
    {
        _connection = new DeviceConnection(_adb, NullLogger<DeviceConnection>.Instance, pollInterval: TimeSpan.Zero);
        _moonlight = new MoonlightClient(_connection, NullLogger<MoonlightClient>.Instance);
    }

    private async Task ConnectWifi()
    {
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial)));
        Assert.True((await _connection.ConnectAsync(WifiSerial, default)).IsSuccess);
    }

    [Fact]
    public async Task Reports_installed_when_pm_list_contains_the_package()
    {
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} {PmList}", stdout: "package:com.limelight\n");

        Assert.True(await _moonlight.IsInstalledAsync(default));
    }

    [Fact]
    public async Task Reports_not_installed_when_pm_list_is_empty_and_never_installs()
    {
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} {PmList}", stdout: "");

        Assert.False(await _moonlight.IsInstalledAsync(default));

        Assert.DoesNotContain(_adb.Calls, c =>
            c.Contains("install", StringComparison.OrdinalIgnoreCase)
            || c.Contains(" pm install", StringComparison.OrdinalIgnoreCase)
            || c.Contains("pm uninstall", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, _adb.CountOf($"-s {WifiSerial} {PmList}"));
    }

    [Fact]
    public async Task Reports_not_installed_when_the_pm_list_command_fails()
    {
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} {PmList}", stderr: "pm: command failed", exitCode: 1);

        Assert.False(await _moonlight.IsInstalledAsync(default));
    }

    [Fact]
    public async Task Reports_not_installed_when_the_queue_fails()
    {
        await ConnectWifi();
        _adb.OnQueueFailure($"-s {WifiSerial} {PmList}");

        Assert.False(await _moonlight.IsInstalledAsync(default));
    }

    [Fact]
    public async Task Reports_not_installed_when_the_thor_is_disconnected()
    {
        Assert.False(await _moonlight.IsInstalledAsync(default));
        Assert.Empty(_adb.Calls);
    }

    [Fact]
    public async Task Launch_opens_the_app_via_the_launcher_intent_without_leaking_the_ip()
    {
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} {Launch}", stdout: "Events injected: 1\n");

        var result = await _moonlight.LaunchAsync("192.168.1.17", default);

        Assert.True(result.IsSuccess);
        var launchCall = Assert.Single(_adb.Calls, c => c.Contains("monkey"));
        Assert.Equal($"-s {WifiSerial} {Launch}", launchCall);
        Assert.Equal(Launch, launchCall[$"-s {WifiSerial} ".Length..]);
    }

    [Fact]
    public async Task Launch_fails_cleanly_when_the_command_exits_non_zero()
    {
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} {Launch}", stderr: "monkey aborted", exitCode: 1);

        var result = await _moonlight.LaunchAsync("192.168.1.17", default);

        Assert.False(result.IsSuccess);
        Assert.Equal("MOONLIGHT_LAUNCH_FAILED", result.Error!.Code);
    }

    [Fact]
    public async Task Launch_fails_when_the_thor_is_disconnected()
    {
        var result = await _moonlight.LaunchAsync("192.168.1.17", default);

        Assert.False(result.IsSuccess);
        Assert.Equal("MOONLIGHT_LAUNCH_FAILED", result.Error!.Code);
        Assert.Empty(_adb.Calls);
    }
}
