using AynThorManager.Services.Connection;
using AynThorManager.Services.Streaming;
using AynThorManager.Services.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static AynThorManager.Services.Tests.Fakes.AdbOutputs;

namespace AynThorManager.Services.Tests.Streaming;

/// <summary>
/// The Moonlight client only ever verifies and opens the official app on the Thor —
/// it never installs anything (the app is an orchestrator, not an installer). Every
/// command rides the shared <see cref="DeviceConnection"/>, so it carries the exact
/// serial (<c>-s {serial}</c>) and needs a connected device first, exactly like the
/// rest of the ADB surface.
/// </summary>
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

    /// <summary>Drives the shared connection into a live Wi-Fi session, like the real flow.</summary>
    private async Task ConnectWifi()
    {
        _adb.On($"connect {WifiSerial}", ConnectedTo(WifiSerial))
            .On("devices -l", Devices(WifiLine(WifiSerial)));
        Assert.True((await _connection.ConnectAsync(WifiSerial, default)).IsSuccess);
    }

    // ---- IsInstalledAsync ----

    [Fact]
    public async Task Reports_installed_when_pm_list_contains_the_package()
    {
        // Req 2.2: verify com.limelight via `pm list packages`.
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} {PmList}", stdout: "package:com.limelight\n");

        Assert.True(await _moonlight.IsInstalledAsync(default));
    }

    [Fact]
    public async Task Reports_not_installed_when_pm_list_is_empty_and_never_installs()
    {
        // Req 2.4: not installed → false, and the app must never attempt an install.
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} {PmList}", stdout: "");

        Assert.False(await _moonlight.IsInstalledAsync(default));

        // No install-like adb traffic ever leaves the client.
        Assert.DoesNotContain(_adb.Calls, c =>
            c.Contains("install", StringComparison.OrdinalIgnoreCase)
            || c.Contains(" pm install", StringComparison.OrdinalIgnoreCase)
            || c.Contains("pm uninstall", StringComparison.OrdinalIgnoreCase));
        // Only the connect/poll handshake plus the single pm-list probe reached adb.
        Assert.Equal(1, _adb.CountOf($"-s {WifiSerial} {PmList}"));
    }

    [Fact]
    public async Task Reports_not_installed_when_the_pm_list_command_fails()
    {
        // A shell error (not a connection drop) must mean "not confirmed", never a throw.
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} {PmList}", stderr: "pm: command failed", exitCode: 1);

        Assert.False(await _moonlight.IsInstalledAsync(default));
    }

    [Fact]
    public async Task Reports_not_installed_when_the_queue_fails()
    {
        // Queue/executor failure (cancellation, crash) is swallowed into false.
        await ConnectWifi();
        _adb.OnQueueFailure($"-s {WifiSerial} {PmList}");

        Assert.False(await _moonlight.IsInstalledAsync(default));
    }

    [Fact]
    public async Task Reports_not_installed_when_the_thor_is_disconnected()
    {
        // No ConnectWifi: the shared connection refuses before anything reaches adb.
        Assert.False(await _moonlight.IsInstalledAsync(default));
        Assert.Empty(_adb.Calls);
    }

    // ---- LaunchAsync ----

    [Fact]
    public async Task Launch_opens_the_app_via_the_launcher_intent_without_leaking_the_ip()
    {
        // Req 4.2: open com.limelight via ADB. Req 4.3: the official app can't take the
        // host through an intent, so the IP is guidance only and never hits adb.
        await ConnectWifi();
        _adb.On($"-s {WifiSerial} {Launch}", stdout: "Events injected: 1\n");

        var result = await _moonlight.LaunchAsync("192.168.1.17", default);

        Assert.True(result.IsSuccess);
        // The intent is activity-agnostic and carries no host — only the fixed launch args.
        var launchCall = Assert.Single(_adb.Calls, c => c.Contains("monkey"));
        Assert.Equal($"-s {WifiSerial} {Launch}", launchCall);
        // The guidance IP is not injected into the Moonlight launch intent (the serial
        // happens to embed the endpoint, but the intent args themselves never carry a host).
        Assert.Equal(Launch, launchCall[$"-s {WifiSerial} ".Length..]);
    }

    [Fact]
    public async Task Launch_fails_cleanly_when_the_command_exits_non_zero()
    {
        // A benign shell error (not a drop pattern) keeps the connection but fails the launch.
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
