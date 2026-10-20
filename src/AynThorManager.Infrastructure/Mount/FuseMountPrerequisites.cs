using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Processes;

namespace AynThorManager.Infrastructure.Mount;

public sealed class FuseMountPrerequisites : IMountPrerequisites
{
    private static readonly string[] FusermountBinaries = ["fusermount3", "fusermount"];

    private const string FuseGuidance =
        "O FUSE (fusermount) não foi encontrado no PC. Instale o pacote de FUSE em espaço de " +
        "usuário (no Debian: fuse3) para montar sem sudo. O app não instala nada automaticamente.";

    private readonly CommandRunner _run;

    public FuseMountPrerequisites() : this(ProcessRunner.RunAsync)
    {
    }

    internal FuseMountPrerequisites(CommandRunner run) => _run = run;

    public async Task<IReadOnlyList<MountPrerequisite>> CheckAsync(CancellationToken ct = default)
    {
        var found = await ProcessRunner.WhichAsync(_run, FusermountBinaries, ct);
        var fuse = found is not null
            ? new MountPrerequisite("FUSE disponível", true)
            : new MountPrerequisite("FUSE disponível", false, FuseGuidance);

        return [fuse];
    }
}
