using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Streaming;

/// <summary>
/// Verifies PC-side prerequisites for reverse streaming (PC → Thor) by running small
/// shell checks and reporting each as a <see cref="Prerequisite"/>. This class only
/// inspects and guides — it never installs anything and never asks for sudo (Req 2).
///
/// Each check is a shell command; the actual command execution goes through a small
/// injectable seam (<see cref="CommandRunner"/>) so tests can drive the checks with
/// fake command outputs. The default constructor wires the real CliWrap runner, mirroring
/// the helper used by <c>WifiConnectionService</c>.
/// </summary>
public sealed class SunshinePrerequisites : ISunshinePrerequisites
{
    /// <summary>Official Sunshine download page, shown as guidance when it is missing.</summary>
    private const string SunshineDownloadUrl = "https://github.com/LizardByte/Sunshine";

    private readonly CommandRunner _run;

    /// <summary>Runs the checks against the real system via CliWrap.</summary>
    public SunshinePrerequisites() : this(RealRun)
    {
    }

    /// <summary>
    /// Test seam: injects how shell commands are executed, so the checks can be driven
    /// with fake outputs without touching the real system.
    /// </summary>
    internal SunshinePrerequisites(CommandRunner run) => _run = run;

    /// <summary>
    /// Runs all PC-side checks and returns one <see cref="Prerequisite"/> per check:
    /// "Sunshine installed?" (Req 2), "hardware encoder available?" (Req 5,
    /// best-effort) and "Wayland capture via Portal?" (Req 6, best-effort). The
    /// "already running?" state is intentionally NOT a prerequisite: the app itself
    /// starts Sunshine, so "not running" before launch is normal, not a warning —
    /// a real failure to come up surfaces as SUNSHINE_START_FAILED after the start.
    /// </summary>
    public async Task<IReadOnlyList<Prerequisite>> CheckAsync(CancellationToken ct = default)
    {
        return
        [
            await CheckInstalledAsync(ct),
            await CheckEncoderAsync(ct),
            await CheckWaylandCaptureAsync(ct),
        ];
    }

    /// <summary>
    /// Req 2.1/2.3: is the Sunshine binary on the system? Looks it up with
    /// <c>which sunshine</c>; when absent, guides the user to the official download
    /// without attempting any install.
    /// </summary>
    private async Task<Prerequisite> CheckInstalledAsync(CancellationToken ct)
    {
        var result = await _run("which", new[] { "sunshine" }, ct);
        var installed = result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput);

        return installed
            ? new Prerequisite("Sunshine instalado", true)
            : new Prerequisite(
                "Sunshine instalado",
                false,
                "O Sunshine não foi encontrado no PC. Instale-o pela página oficial e tente novamente.",
                SunshineDownloadUrl);
    }

    /// <summary>
    /// Req 5: best-effort detection of hardware (VAAPI) vs software encode. Sunshine
    /// picks the encoder automatically (Req 5.1), so instead of parsing its log we probe
    /// the host for VAAPI encode support with <c>vainfo</c>: an encode entrypoint
    /// (<c>VAEntrypointEncSlice</c>/<c>VAEntrypointEncSliceLP</c>) means hardware encode
    /// is available. When <c>vainfo</c> is missing, fails, or shows no encode
    /// entrypoints, we flag a likely software-fallback risk and warn about the
    /// performance impact (Req 5.2). This is detection/warning only — it NEVER blocks
    /// install; a failed or absent tool yields an informative, non-blocking result.
    /// </summary>
    private async Task<Prerequisite> CheckEncoderAsync(CancellationToken ct)
    {
        var result = await _run("vainfo", Array.Empty<string>(), ct);

        // vainfo lists supported entrypoints; an encode slice entrypoint means the
        // iGPU can encode in hardware. Tolerant match: either success exit or just the
        // presence of the entrypoint in whatever output we got.
        var hasHardwareEncode =
            result.StandardOutput.Contains("VAEntrypointEncSlice", StringComparison.Ordinal);

        return hasHardwareEncode
            ? new Prerequisite("Encode por hardware (VAAPI)", true)
            : new Prerequisite(
                "Encode por hardware (VAAPI)",
                false,
                "Não foi possível confirmar o encode por hardware (VAAPI) no PC. O Sunshine " +
                "pode cair para encode por software, o que degrada bastante a performance em " +
                "jogos. Verifique os drivers da GPU e o pacote de VAAPI (ex: vainfo).");
    }

    /// <summary>
    /// Req 6: best-effort check for Wayland screen capture via the xdg-desktop-portal
    /// (the no-sudo path, Req 6.1). The portal is considered available when ANY of
    /// these holds: the binary is on <c>PATH</c> (<c>which</c>), it exists at a known
    /// location (<c>/usr/libexec/xdg-desktop-portal</c> or
    /// <c>/usr/lib/xdg-desktop-portal</c> — where Debian/Fedora ship it, off <c>PATH</c>),
    /// or the portal process is already running. A single <c>sh -c</c> rolls those into
    /// one exit code. When none confirm it, we guide the user without ever running sudo
    /// (Req 6.2). Best-effort and non-crashing: a failure sentinel just produces guidance.
    /// </summary>
    private async Task<Prerequisite> CheckWaylandCaptureAsync(CancellationToken ct)
    {
        // exit 0 if the portal is on PATH, installed at a known libexec/lib path, or
        // currently running; non-zero otherwise. Kept as one scriptable command so the
        // test seam can fake it with a single entry.
        const string probe =
            "command -v xdg-desktop-portal >/dev/null 2>&1 || " +
            "test -x /usr/libexec/xdg-desktop-portal || " +
            "test -x /usr/lib/xdg-desktop-portal || " +
            "pgrep -f xdg-desktop-portal >/dev/null 2>&1";
        var result = await _run("sh", new[] { "-c", probe }, ct);
        var portalAvailable = result.ExitCode == 0;

        return portalAvailable
            ? new Prerequisite("Captura via Portal (Wayland)", true)
            : new Prerequisite(
                "Captura via Portal (Wayland)",
                false,
                "A captura de tela via Portal (xdg-desktop-portal) não parece disponível. " +
                "Instale/ative o xdg-desktop-portal para autorizar a captura sem sudo. " +
                "O app não executa sudo automaticamente.");
    }

    /// <summary>Runs a command via CliWrap, mirroring the helper in <c>WifiConnectionService</c>.</summary>
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

/// <summary>
/// Executes a shell command and returns its outcome. The real implementation wraps
/// CliWrap; tests supply a fake to feed canned command outputs into the checks. Shared
/// seam for all <see cref="SunshinePrerequisites"/> checks (reused by later checks).
/// </summary>
internal delegate Task<CommandOutput> CommandRunner(
    string file, IReadOnlyList<string> args, CancellationToken ct);

/// <summary>Minimal result of a shell command: exit code and captured output streams.</summary>
internal readonly record struct CommandOutput(
    int ExitCode,
    string StandardOutput,
    string StandardError);
