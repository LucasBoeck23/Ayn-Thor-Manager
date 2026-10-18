using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using AynThorManager.Services.Streaming;
using AynThorManager.Services.Telemetry;
using AynThorManager.Services.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static AynThorManager.Services.Tests.Fakes.AdbOutputs;

namespace AynThorManager.Services.Tests.Streaming;

/// <summary>
/// Orchestrator tests for <see cref="ReverseStreamingService"/> (PC → Thor). The host,
/// PC-side prerequisites and forward scrcpy mirror are faked; the real
/// <see cref="MoonlightClient"/> and <see cref="StreamingService"/> are driven through
/// the shared <see cref="DeviceConnection"/> on <see cref="FakeAdb"/>, exactly like the
/// production wiring. Retries run with a zero delay (internal ctor) so the fallback
/// paths exercise instantly (Req 9.2).
/// </summary>
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

    /// <summary>Builds the service under test with a zero retry delay for fast fallbacks.</summary>
    private ReverseStreamingService CreateService() =>
        new(_connection, _forward, _prereqs, _moonlight, _host,
            NullLogger<ReverseStreamingService>.Instance, retryDelay: TimeSpan.Zero);

    /// <summary>Drives the shared connection into a live Wi-Fi session, like the real flow.</summary>
    private async Task ConnectWifi()
    {
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial)));
        Assert.True((await _connection.ConnectAsync(WifiSerial, default)).IsSuccess);
    }

    /// <summary>Scripts Moonlight as installed (pm list returns the package).</summary>
    private void MoonlightInstalled() =>
        _adb.On($"-s {WifiSerial} {PmList}", stdout: "package:com.limelight\n");

    /// <summary>Scripts Moonlight as not installed (empty pm list).</summary>
    private void MoonlightNotInstalled() =>
        _adb.On($"-s {WifiSerial} {PmList}", stdout: "");

    /// <summary>Scripts a single successful Moonlight launch.</summary>
    private void MoonlightLaunchOk() =>
        _adb.On($"-s {WifiSerial} {Launch}", stdout: "Events injected: 1\n");

    // ---- Prerequisites / availability (Req 1) ----

    [Fact]
    public async Task CheckPrerequisites_without_a_thor_returns_a_single_not_ok_thor_connected()
    {
        // Req 1.5: no Thor connected → the reverse action is unavailable.
        var service = CreateService();

        var prereqs = await service.CheckPrerequisitesAsync(default);

        var only = Assert.Single(prereqs);
        Assert.Equal("Thor conectado", only.Name);
        Assert.False(only.Ok);
        // The device-dependent checks are skipped entirely (no PC checks, no adb).
        Assert.Equal(0, _prereqs.CallCount);
        Assert.Empty(_adb.Calls);
    }

    [Fact]
    public async Task Start_without_a_thor_fails_with_prerequisites_missing_and_never_starts_sunshine()
    {
        // Req 1.5 + gate: the Thor-connected prerequisite is missing, so Start is blocked.
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
        // Req 1: a connected Thor with all PC checks ok and Moonlight installed → all Ok,
        // and the Moonlight "installed" prerequisite shows up and is ok.
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
        // Req 1.4 / 2.5: a not-ok PC prerequisite blocks the start; nothing is installed
        // and Sunshine is never launched. The message names what's missing.
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
        // Req 2.4: Moonlight missing on the Thor → the start is blocked, no install, and
        // Sunshine is never started.
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
        // Req 5.2 / 6: the hardware-encoder check is best-effort and must NEVER block the
        // start — a not-ok encoder only warns (software-fallback performance caveat). With
        // every blocking prerequisite satisfied, the start succeeds and Sunshine runs even
        // though the encoder check is not ok.
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

    // ---- Mutual exclusion (Req 7) ----

    [Fact]
    public async Task Start_stops_the_forward_direction_first_then_succeeds()
    {
        // Req 7: the two directions never run at once — starting the reverse ends scrcpy.
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        _mirror.IsRunning = true; // forward (scrcpy) is mirroring
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _mirror.StopCount);
        Assert.False(_forward.IsRunning);
    }

    // ---- Happy path (Req 1.3 / 3) ----

    [Fact]
    public async Task Start_happy_path_starts_sunshine_launches_moonlight_and_goes_active()
    {
        // Req 1.3 / 3.1 / 4.2: Sunshine not running → start it once, open Moonlight, and
        // raise StateChanged(Active).
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
        // Req 3.2: Sunshine already up → never start a second instance; still succeeds.
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        _host.IsRunning = true; // Sunshine already running (e.g. user started it)
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _host.StartCount);
        Assert.True(service.IsRunning);
    }

    [Fact]
    public async Task Start_returns_sunshine_start_failed_when_the_host_throws()
    {
        // Req 3 / 9.2: the host binary can't be launched → specific error, retryable.
        await ConnectWifi();
        MoonlightInstalled();
        _host.StartThrows = new InvalidOperationException("sunshine not found");
        var service = CreateService();

        var result = await service.StartAsync(ct: default);

        Assert.False(result.IsSuccess);
        Assert.Equal("SUNSHINE_START_FAILED", result.Error!.Code);
    }

    // ---- Fallbacks (Req 9) ----

    [Fact]
    public async Task Start_retries_moonlight_and_succeeds_on_the_third_attempt()
    {
        // Req 9.2: the ADB launch can be flaky right after the hotspot settles. Two
        // transient failures then a success → overall success, three launch attempts.
        await ConnectWifi();
        MoonlightInstalled();
        // FakeAdb consumes responses in order (last repeats): fail, fail, succeed.
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
        // Req 9.2 / 9.3: all launch attempts fail → MOONLIGHT_LAUNCH_FAILED, and the
        // Sunshine we started here is stopped so no orphan is left behind.
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
        // Req 9.1 / 9.3: start successfully, then the Thor falls off its own hotspot.
        // A device-lost adb reply fires the real DeviceConnection.StateChanged(null),
        // which the orchestrator turns into an error state and a host teardown.
        await ConnectWifi();
        MoonlightInstalled();
        MoonlightLaunchOk();
        var service = CreateService();

        Assert.True((await service.StartAsync(ct: default)).IsSuccess);
        Assert.True(service.IsRunning);

        var statuses = new List<ReverseStreamStatus>();
        service.StateChanged += statuses.Add;

        // Drive the real StateChanged(null): a command whose stderr matches the
        // device-lost pattern makes DeviceConnection clear the current device.
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
        // Req 3.3: when the Sunshine process ends, reflect an inactive reverse stream.
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
        // Req 9.3 / 3.4: closing the window (or ending the reverse direction) calls
        // Stop(), which must kill the Sunshine the app started so none is left orphaned.
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
        // Req 9.3: the window-close path calls Stop() unconditionally. It must be a no-op
        // safe call when no reverse stream was ever started — it still forwards to the
        // idle host (whose own Stop is a no-op) and never throws.
        var service = CreateService();

        var ex = Record.Exception(service.Stop);

        Assert.Null(ex);
        Assert.Equal(1, _host.StopCount);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Start_is_restartable_after_a_failed_attempt()
    {
        // Req 9.2: a failed start leaves the service re-startable; a later all-ok start
        // succeeds cleanly.
        await ConnectWifi();
        MoonlightInstalled();
        // First attempt: Moonlight launch fails every time.
        _adb.On($"-s {WifiSerial} {Launch}", stderr: "monkey aborted", exitCode: 1);
        var service = CreateService();

        var first = await service.StartAsync(ct: default);
        Assert.False(first.IsSuccess);
        Assert.Equal("MOONLIGHT_LAUNCH_FAILED", first.Error!.Code);
        Assert.False(service.IsRunning);

        // Second attempt: the launch now succeeds (FakeAdb's last response repeats, so
        // append a success that will be consumed next).
        _adb.On($"-s {WifiSerial} {Launch}", stdout: "Events injected: 1\n");

        var second = await service.StartAsync(ct: default);
        Assert.True(second.IsSuccess);
        Assert.True(service.IsRunning);
    }
}
