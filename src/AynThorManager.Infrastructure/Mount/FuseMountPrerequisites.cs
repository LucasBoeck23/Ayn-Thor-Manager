using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Streaming;

namespace AynThorManager.Infrastructure.Mount;

public sealed class FuseMountPrerequisites : IMountPrerequisites
{
    private static readonly string[] FusermountBinaries = ["fusermount3", "fusermount"];

    private const string FuseGuidance =
        "O FUSE (fusermount) não foi encontrado no PC. Instale o pacote de FUSE em espaço de " +
        "usuário (no Debian: fuse3) para montar sem sudo. O app não instala nada automaticamente.";

    private readonly CommandRunner _run;

    public FuseMountPrerequisites() : this(RealRun)
    {
    }

    internal FuseMountPrerequisites(CommandRunner run) => _run = run;

    public async Task<IReadOnlyList<MountPrerequisite>> CheckAsync(CancellationToken ct = default)
    {
        return [await CheckFuseAsync(ct)];
    }

    private async Task<MountPrerequisite> CheckFuseAsync(CancellationToken ct)
    {
        var found = await WhichAnyAsync(FusermountBinaries, ct);
        return found is not null
            ? new MountPrerequisite("FUSE disponível", true)
            : new MountPrerequisite("FUSE disponível", false, FuseGuidance);
    }

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
