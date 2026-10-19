using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Streaming;

namespace AynThorManager.Infrastructure.Mount;

/// <summary>
/// The Infrastructure implementation of <see cref="IMountPrerequisites"/> for the native
/// FUSE→ADB mount. Because the translator is our own code (no third-party <c>adbfs</c>
/// binary), the only host-side prerequisite is FUSE: a mount helper
/// (<c>fusermount3</c>/<c>fusermount</c>) on <c>PATH</c> (Req 3.2/3.3). This class only
/// inspects and guides — it never installs anything and never asks for sudo (Req 2); when
/// FUSE is absent it points the user at the distro's FUSE package.
///
/// It stands on its own (it does not depend on <c>AdbfsMount</c>), so removing the old
/// adbfs mount is a clean deletion. The "Thor connected?" check is intentionally not here:
/// it belongs to the Services orchestrator, which owns the <c>DeviceConnection</c>
/// (Req 3.4).
///
/// Command execution goes through the same injectable CliWrap seam used by
/// <see cref="SunshinePrerequisites"/> (<see cref="CommandRunner"/> / <see cref="CommandOutput"/>),
/// so the detection logic can be driven with fake command outputs without touching the
/// real system.
/// </summary>
public sealed class FuseMountPrerequisites : IMountPrerequisites
{
    /// <summary>Candidate names of the FUSE mount helper, checked in order (fuse3 first).</summary>
    private static readonly string[] FusermountBinaries = ["fusermount3", "fusermount"];

    /// <summary>
    /// Guidance shown when no FUSE mount helper is on PATH. The app orients, it never
    /// installs: on Debian the package is <c>fuse3</c>.
    /// </summary>
    private const string FuseGuidance =
        "O FUSE (fusermount) não foi encontrado no PC. Instale o pacote de FUSE em espaço de " +
        "usuário (no Debian: fuse3) para montar sem sudo. O app não instala nada automaticamente.";

    private readonly CommandRunner _run;

    /// <summary>Runs the check against the real system via CliWrap.</summary>
    public FuseMountPrerequisites() : this(RealRun)
    {
    }

    /// <summary>
    /// Test seam: injects how commands are executed, so the FUSE detection can be driven
    /// with fake outputs without spawning real processes.
    /// </summary>
    internal FuseMountPrerequisites(CommandRunner run) => _run = run;

    /// <summary>
    /// Req 3.2/3.3: best-effort detection of FUSE without installing anything. Returns a
    /// single <see cref="MountPrerequisite"/> — "FUSE available?" — looking up
    /// <c>fusermount3</c>/<c>fusermount</c> on PATH and carrying guidance when it is
    /// missing. The Thor-connected check belongs to the Services orchestrator, which owns
    /// the <c>DeviceConnection</c> (Req 3.4).
    /// </summary>
    public async Task<IReadOnlyList<MountPrerequisite>> CheckAsync(CancellationToken ct = default)
    {
        return [await CheckFuseAsync(ct)];
    }

    /// <summary>Req 3.2/3.3: is a FUSE mount helper on PATH? Guides to install FUSE when absent.</summary>
    private async Task<MountPrerequisite> CheckFuseAsync(CancellationToken ct)
    {
        var found = await WhichAnyAsync(FusermountBinaries, ct);
        return found is not null
            ? new MountPrerequisite("FUSE disponível", true)
            : new MountPrerequisite("FUSE disponível", false, FuseGuidance);
    }

    /// <summary>
    /// Returns the first of <paramref name="candidates"/> that <c>which</c> resolves on
    /// PATH, or <c>null</c> if none are present. Best-effort and non-throwing.
    /// </summary>
    private async Task<string?> WhichAnyAsync(IReadOnlyList<string> candidates, CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            var result = await _run("which", [candidate], ct);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Runs a command via CliWrap. Never throws: a launch failure is reported as a sentinel
    /// exit code so the check surfaces cleanly instead of crashing. Mirrors the shared
    /// helper used by <see cref="SunshinePrerequisites"/>.
    /// </summary>
    private static async Task<CommandOutput> RealRun(string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            var result = await Cli.Wrap(file)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            return new CommandOutput(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (Exception ex)
        {
            return new CommandOutput(-1, string.Empty, ex.Message);
        }
    }
}
