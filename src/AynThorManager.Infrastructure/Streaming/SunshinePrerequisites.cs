using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Processes;

namespace AynThorManager.Infrastructure.Streaming;

public sealed class SunshinePrerequisites : ISunshinePrerequisites
{
    private const string SunshineDownloadUrl = "https://github.com/LizardByte/Sunshine";

    private readonly CommandRunner _run;

    public SunshinePrerequisites() : this(ProcessRunner.RunAsync)
    {
    }

    internal SunshinePrerequisites(CommandRunner run) => _run = run;

    public async Task<IReadOnlyList<Prerequisite>> CheckAsync(CancellationToken ct = default)
    {
        return
        [
            await CheckInstalledAsync(ct),
            await CheckEncoderAsync(ct),
            await CheckWaylandCaptureAsync(ct),
        ];
    }

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

    private async Task<Prerequisite> CheckEncoderAsync(CancellationToken ct)
    {
        var result = await _run("vainfo", Array.Empty<string>(), ct);

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

    private async Task<Prerequisite> CheckWaylandCaptureAsync(CancellationToken ct)
    {
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
}
