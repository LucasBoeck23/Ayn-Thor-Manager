using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Streaming;

namespace AynThorManager.Infrastructure.Tests.Streaming;

/// <summary>
/// Drives <see cref="SunshinePrerequisites.CheckAsync"/> through its command-runner
/// seam with canned <see cref="CommandOutput"/>s, so each of the three checks
/// (installed / encoder / wayland-capture) is exercised without touching
/// the real system (Req 2, Req 5, Req 6).
/// </summary>
public class SunshinePrerequisitesTests
{
    private const string SunshineDownloadUrl = "https://github.com/LizardByte/Sunshine";

    // Canned outputs per check, defaulting to a fully-healthy system. Each test
    // overrides only the output it cares about.
    private CommandOutput _which = new(0, "/usr/bin/sunshine", string.Empty);
    private CommandOutput _vainfo = new(0, "VAProfileH264Main : VAEntrypointEncSlice", string.Empty);
    private CommandOutput _portal = new(0, string.Empty, string.Empty);

    /// <summary>
    /// Fake runner: dispatches on the command and args to the right canned output.
    /// </summary>
    private SunshinePrerequisites Create() => new((file, args, _) =>
    {
        CommandOutput output = (file, args.Count > 0 ? args[0] : string.Empty) switch
        {
            ("which", "sunshine") => _which,
            ("vainfo", _) => _vainfo,
            ("which", "xdg-desktop-portal") => _portal,
            ("sh", _) => _portal,
            _ => new CommandOutput(-1, string.Empty, "unexpected command"),
        };
        return Task.FromResult(output);
    });

    private static Prerequisite Installed(IReadOnlyList<Prerequisite> p) => p[0];
    private static Prerequisite Encoder(IReadOnlyList<Prerequisite> p) => p[1];
    private static Prerequisite WaylandCapture(IReadOnlyList<Prerequisite> p) => p[2];

    [Fact]
    public async Task All_prerequisites_ok_when_every_check_is_healthy()
    {
        // Req 2/5/6 happy path.
        var result = await Create().CheckAsync();

        Assert.Equal(3, result.Count);
        Assert.All(result, p => Assert.True(p.Ok));
    }

    [Fact]
    public async Task Checks_are_returned_in_order()
    {
        var result = await Create().CheckAsync();

        Assert.Equal("Sunshine instalado", result[0].Name);
        Assert.Equal("Encode por hardware (VAAPI)", result[1].Name);
        Assert.Equal("Captura via Portal (Wayland)", result[2].Name);
    }

    [Fact]
    public async Task Sunshine_not_installed_when_which_exits_nonzero()
    {
        // Req 2.3: not found → guidance + official URL, no install attempt.
        _which = new CommandOutput(1, string.Empty, string.Empty);

        var installed = Installed(await Create().CheckAsync());

        Assert.False(installed.Ok);
        Assert.False(string.IsNullOrWhiteSpace(installed.Guidance));
        Assert.Equal(SunshineDownloadUrl, installed.Url);
    }

    [Fact]
    public async Task Sunshine_not_installed_when_which_output_is_empty()
    {
        // Exit 0 but empty stdout still means "not found".
        _which = new CommandOutput(0, "   ", string.Empty);

        var installed = Installed(await Create().CheckAsync());

        Assert.False(installed.Ok);
        Assert.Equal(SunshineDownloadUrl, installed.Url);
    }

    [Fact]
    public async Task Software_encode_fallback_risk_when_vainfo_has_no_encode_entrypoint()
    {
        // Req 5.2: no VAAPI encode entrypoint → warn about software-encode performance.
        _vainfo = new CommandOutput(0, "VAProfileH264Main : VAEntrypointVLD", string.Empty);

        var encoder = Encoder(await Create().CheckAsync());

        Assert.False(encoder.Ok);
        Assert.NotNull(encoder.Guidance);
        Assert.Contains("software", encoder.Guidance!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("performance", encoder.Guidance!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Software_encode_fallback_risk_when_vainfo_is_missing()
    {
        // vainfo not installed → runner failure sentinel, still non-blocking.
        _vainfo = new CommandOutput(-1, string.Empty, "command not found");

        var encoder = Encoder(await Create().CheckAsync());

        Assert.False(encoder.Ok);
        Assert.NotNull(encoder.Guidance);
    }

    [Fact]
    public async Task Portal_unavailable_guidance_never_instructs_sudo()
    {
        // Req 6.2: portal missing → guidance, and explicitly no automatic sudo.
        _portal = new CommandOutput(1, string.Empty, string.Empty);

        var portal = WaylandCapture(await Create().CheckAsync());

        Assert.False(portal.Ok);
        Assert.NotNull(portal.Guidance);
        // Guidance must not tell the user that the app runs sudo for them.
        Assert.DoesNotContain("sudo apt", portal.Guidance!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("executa sudo automaticamente para você", portal.Guidance!, StringComparison.OrdinalIgnoreCase);
        // It should affirm the no-sudo path instead.
        Assert.Contains("sem sudo", portal.Guidance!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Portal_available_when_found_at_a_known_path_off_PATH()
    {
        // Regression: on Debian the binary lives at /usr/libexec/xdg-desktop-portal,
        // which is NOT on PATH. The probe (sh -c) still exits 0 because it also tests the
        // known libexec/lib paths and the running process — so capture is reported ok.
        _portal = new CommandOutput(0, string.Empty, string.Empty);

        var portal = WaylandCapture(await Create().CheckAsync());

        Assert.True(portal.Ok);
    }

    [Fact]
    public async Task Portal_unavailable_when_probe_exits_nonzero()
    {
        // Not on PATH, not at a known path, and not running → probe exits non-zero.
        _portal = new CommandOutput(1, string.Empty, string.Empty);

        var portal = WaylandCapture(await Create().CheckAsync());

        Assert.False(portal.Ok);
    }

    [Fact]
    public async Task Failure_sentinel_does_not_throw_and_yields_nonblocking_results()
    {
        // Best-effort tolerance: every command returns the runner's -1 failure sentinel.
        _which = new CommandOutput(-1, string.Empty, "boom");
        _vainfo = new CommandOutput(-1, string.Empty, "boom");
        _portal = new CommandOutput(-1, string.Empty, "boom");

        var result = await Create().CheckAsync();

        Assert.Equal(3, result.Count);
        Assert.All(result, p => Assert.False(p.Ok));
    }
}
