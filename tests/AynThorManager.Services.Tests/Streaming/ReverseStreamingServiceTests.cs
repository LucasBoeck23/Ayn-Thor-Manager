using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using AynThorManager.Services.Streaming;
using AynThorManager.Services.Telemetry;
using AynThorManager.Services.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static AynThorManager.Services.Tests.Fakes.AdbOutputs;

namespace AynThorManager.Services.Tests.Streaming;

public class ReverseStreamingServiceTests
{
    private const string PmList = "shell pm list packages com.limelight";
    private const string Launch = "shell monkey -p com.limelight -c android.intent.category.LAUNCHER 1";

    private readonly FakeAdb _adb = new();
    private readonly DeviceConnection _connection;
    private readonly MoonlightClient _moonlight;

    private readonly FakeScreenMirror _mirror = new();
    private readonly FakeGameStreamHost _host = new();
    private readonly FakeSunshinePrerequisites _prereqs = new();
    private readonly StreamingService _forward;

    public ReverseStreamingServiceTests()
    {
        _connection = new DeviceConnection(_adb, NullLogger<DeviceConnection>.Instance, pollInterval: TimeSpan.Zero);
        _moonlight = new MoonlightClient(_connection, NullLogger<MoonlightClient>.Instance);

        var monitor = new NetworkMonitor(_connection, NullLogger<NetworkMonitor>.Instance);
        _forward = new StreamingService(_connection, _mirror, monitor, NullLogger<StreamingService>.Instance);
    }

    private ReverseStreamingService CreateService() =>
        new(_connection, _forward, _prereqs, _moonlight, _host,
            NullLogger<ReverseStreamingService>.Instance, retryDelay: TimeSpan.Zero);

    private async Task ConnectWifi()
    {
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial)));
        Assert.True((await _connection.ConnectAsync(WifiSerial, default)).IsSuccess);
    }

    private void MoonlightInstalled() =>
        _adb.On($"-s {WifiSerial} {PmList}", stdout: "package:com.limelight\n");

    private void MoonlightNotInstalled() =>
        _adb.On($"-s {WifiSerial} {PmList}", stdout: "");

    private void MoonlightLaunchOk() =>
        _adb.On($"-s {WifiSerial} {Launch}", stdout: "Events injected: 1\n");

    [Fact]
    public async Task CheckPrerequisites_without_a_thor_returns_a_single_not_ok_thor_connected()
    {
        var service = CreateService();

        var prereqs = await service.CheckPrerequisitesAsync(default);

        var only = Assert.Single(prereqs);
        Assert.Equal("Thor conectado", only.Name);
        Assert.False(only.Ok);
        Assert.Equal(0, _prereqs.CallCount);
        Assert.Empty(_adb.Calls);
    }

    [Fact]
    public async Task Start_without_a_thor_fails_with_prerequisites_missing_and_never_starts_sunshine()
    {
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.False(result.IsSuccess);
        Assert.Equal("REVERSE_PREREQUISITES_MISSING", result.Error!.Code);
        Assert.Contains("Thor conectado", result.Error.Message);
        Assert.Equal(0, _host.StartCount);
    }

    [Fact]
    public async Task CheckPrerequisites_when_everything_is_ok_lists_all_ok_including_moonlight()
    {
        await ConnectWifi();
        MoonlightInstalled();
        var service = CreateService();

        var prereqs = await service.CheckPrerequisitesAsync(default);

        Assert.All(prereqs, p => Assert.True(p.Ok));
        var moonlight = Assert.Single(prereqs, p => p.Name.Contains("Moonlight"));
        Assert.True(moonlight.Ok);
    }

    [Fact]
    public async Task Start_blocked_when_a_pc_prerequisite_is_missing_and_never_starts_sunshine()
    {
        await ConnectWifi();
        MoonlightInstalled();
        _prereqs.WithMissing("Sunshine instalado");
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.False(result.IsSuccess);
        Assert.Equal("REVERSE_PREREQUISITES_MISSING", result.Error!.Code);
        Assert.Contains("Sunshine instalado", result.Error.Message);
        Assert.Equal(0, _host.StartCount);
    }

    [Fact]
    public async Task Start_blocked_when_moonlight_is_not_installed_and_never_starts_sunshine()
    {
        await ConnectWifi();
        MoonlightNotInstalled();
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.False(result.IsSuccess);
        Assert.Equal("REVERSE_PREREQUISITES_MISSING", result.Error!.Code);
        Assert.Contains("Moonlight", result.Error.Message);
        Assert.Equal(0, _host.StartCount);
    }

    [Fact]
    public async Task Start_proceeds_when_only_a_best_effort_prerequisite_is_not_ok()
    {
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        _prereqs.WithSoftwareEncodeWarning();
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _host.StartCount);
        Assert.True(service.IsRunning);
    }

    [Fact]
    public async Task Start_stops_the_forward_direction_first_then_succeeds()
    {
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        _mirror.IsRunning = true;
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _mirror.StopCount);
        Assert.False(_forward.IsRunning);
    }

    [Fact]
    public async Task Start_happy_path_starts_sunshine_launches_moonlight_and_goes_active()
    {
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        var service = CreateService();

        ReverseStreamStatus? status = null;
        service.StateChanged += s => status = s;

        var result = await service.StartAsync(ct: default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _host.StartCount);
        Assert.True(service.IsRunning);
        Assert.Equal(1, _adb.CountOf($"-s {WifiSerial} {Launch}"));
        Assert.NotNull(status);
        Assert.Equal(ReverseStreamState.Active, status!.State);
    }

    [Fact]
    public async Task Start_does_not_start_a_second_sunshine_when_already_running()
    {
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        _host.IsRunning = true;
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _host.StartCount);
        Assert.True(service.IsRunning);
    }

    [Fact]
    public async Task Start_returns_sunshine_start_failed_when_the_host_throws()
    {
        await ConnectWifi();
        MoonlightInstalled();
        _host.StartThrows = new InvalidOperationException("sunshine not found");
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.False(result.IsSuccess);
        Assert.Equal("SUNSHINE_START_FAILED", result.Error!.Code);
    }

    [Fact]
    public async Task Start_retries_moonlight_and_succeeds_on_the_third_attempt()
    {
        await ConnectWifi();
        MoonlightInstalled();
        _adb.On($"-s {WifiSerial} {Launch}", stderr: "monkey aborted", exitCode: 1)
            .On($"-s {WifiSerial} {Launch}", stderr: "monkey aborted", exitCode: 1)
            .On($"-s {WifiSerial} {Launch}", stdout: "Events injected: 1\n");
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, _adb.CountOf($"-s {WifiSerial} {Launch}"));
        Assert.True(service.IsRunning);
    }

    [Fact]
    public async Task Start_fails_and_stops_sunshine_when_every_moonlight_attempt_fails()
    {
        await ConnectWifi();
        MoonlightInstalled();
        _adb.On($"-s {WifiSerial} {Launch}", stderr: "monkey aborted", exitCode: 1);
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.False(result.IsSuccess);
        Assert.Equal("MOONLIGHT_LAUNCH_FAILED", result.Error!.Code);
        Assert.Equal(3, _adb.CountOf($"-s {WifiSerial} {Launch}"));
        Assert.Equal(1, _host.StopCount);
        Assert.False(_host.IsRunning);
    }

    [Fact]
    public async Task Thor_dropping_while_active_tears_down_sunshine_and_reports_error()
    {
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        var service = CreateService();

        Assert.True((await service.StartAsync(ct: default)).IsSuccess);
        Assert.True(service.IsRunning);

        var statuses = new List<ReverseStreamStatus>();
        service.StateChanged += statuses.Add;

        _adb.On($"-s {WifiSerial} shell echo probe", stderr: DeviceOffline);
        var probe = await _connection.RunAsync("shell echo probe", TimeSpan.FromSeconds(1), default);
        Assert.False(probe.IsSuccess);
        Assert.Equal("DEVICE_LOST", probe.Error!.Code);
        Assert.Null(_connection.Current);

        var error = Assert.Single(statuses);
        Assert.Equal(ReverseStreamState.Error, error.State);
        Assert.Equal("O Thor caiu da rede.", error.Reason);
        Assert.Equal(1, _host.StopCount);
    }

    [Fact]
    public async Task Host_exiting_while_active_reports_inactive()
    {
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        var service = CreateService();

        Assert.True((await service.StartAsync(ct: default)).IsSuccess);

        ReverseStreamStatus? status = null;
        service.StateChanged += s => status = s;

        _host.RaiseExited();

        Assert.NotNull(status);
        Assert.Equal(ReverseStreamState.Inactive, status!.State);
    }

    [Fact]
    public async Task Stop_tears_down_an_owned_sunshine_and_leaves_no_running_host()
    {
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        var service = CreateService();

        Assert.True((await service.StartAsync(ct: default)).IsSuccess);
        Assert.True(service.IsRunning);

        service.Stop();

        Assert.Equal(1, _host.StopCount);
        Assert.False(_host.IsRunning);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void Stop_is_safe_when_nothing_is_running()
    {
        var service = CreateService();

        var ex = Record.Exception(service.Stop);

        Assert.Null(ex);
        Assert.Equal(1, _host.StopCount);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Start_is_restartable_after_a_failed_attempt()
    {
        await ConnectWifi();
        MoonlightInstalled();
        _adb.On($"-s {WifiSerial} {Launch}", stderr: "monkey aborted", exitCode: 1);
        var service = CreateService();

        var first = await service.StartAsync(ct: default);
        Assert.False(first.IsSuccess);
        Assert.Equal("MOONLIGHT_LAUNCH_FAILED", first.Error!.Code);
        Assert.False(service.IsRunning);

        _adb.On($"-s {WifiSerial} {Launch}", stdout: "Events injected: 1\n");

        var second = await service.StartAsync(ct: default);
        Assert.True(second.IsSuccess);
        Assert.True(service.IsRunning);
    }
}
