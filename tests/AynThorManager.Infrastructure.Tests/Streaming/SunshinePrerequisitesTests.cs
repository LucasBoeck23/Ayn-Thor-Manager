using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Processes;
using AynThorManager.Infrastructure.Streaming;

namespace AynThorManager.Infrastructure.Tests.Streaming;

public class SunshinePrerequisitesTests
{
    private const string SunshineDownloadUrl = "https://github.com/LizardByte/Sunshine";

    private CommandOutput _which = new(0, "/usr/bin/sunshine", string.Empty);
    private CommandOutput _vainfo = new(0, "VAProfileH264Main : VAEntrypointEncSlice", string.Empty);
    private CommandOutput _portal = new(0, string.Empty, string.Empty);

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
        _which = new CommandOutput(1, string.Empty, string.Empty);

        var installed = Installed(await Create().CheckAsync());

        Assert.False(installed.Ok);
        Assert.False(string.IsNullOrWhiteSpace(installed.Guidance));
        Assert.Equal(SunshineDownloadUrl, installed.Url);
    }

    [Fact]
    public async Task Sunshine_not_installed_when_which_output_is_empty()
    {
        _which = new CommandOutput(0, "   ", string.Empty);

        var installed = Installed(await Create().CheckAsync());

        Assert.False(installed.Ok);
        Assert.Equal(SunshineDownloadUrl, installed.Url);
    }

    [Fact]
    public async Task Software_encode_fallback_risk_when_vainfo_has_no_encode_entrypoint()
    {
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
        _vainfo = new CommandOutput(-1, string.Empty, "command not found");

        var encoder = Encoder(await Create().CheckAsync());

        Assert.False(encoder.Ok);
        Assert.NotNull(encoder.Guidance);
    }

    [Fact]
    public async Task Portal_unavailable_guidance_never_instructs_sudo()
    {
        _portal = new CommandOutput(1, string.Empty, string.Empty);

        var portal = WaylandCapture(await Create().CheckAsync());

        Assert.False(portal.Ok);
        Assert.NotNull(portal.Guidance);
        Assert.DoesNotContain("sudo apt", portal.Guidance!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("executa sudo automaticamente para você", portal.Guidance!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sem sudo", portal.Guidance!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Portal_available_when_found_at_a_known_path_off_PATH()
    {
        _portal = new CommandOutput(0, string.Empty, string.Empty);

        var portal = WaylandCapture(await Create().CheckAsync());

        Assert.True(portal.Ok);
    }

    [Fact]
    public async Task Portal_unavailable_when_probe_exits_nonzero()
    {
        _portal = new CommandOutput(1, string.Empty, string.Empty);

        var portal = WaylandCapture(await Create().CheckAsync());

        Assert.False(portal.Ok);
    }

    [Fact]
    public async Task Failure_sentinel_does_not_throw_and_yields_nonblocking_results()
    {
        _which = new CommandOutput(-1, string.Empty, "boom");
        _vainfo = new CommandOutput(-1, string.Empty, "boom");
        _portal = new CommandOutput(-1, string.Empty, "boom");

        var result = await Create().CheckAsync();

        Assert.Equal(3, result.Count);
        Assert.All(result, p => Assert.False(p.Ok));
    }
}
