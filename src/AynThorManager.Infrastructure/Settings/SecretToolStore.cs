using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Settings;

public sealed class SecretToolStore : ISecretStore
{
    private const string Executable = "secret-tool";
    private const string AppAttribute = "ayn-thor-manager";

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var result = await Cli.Wrap(Executable)
                .WithArguments(new[] { "lookup", "application", AppAttribute, "key", key })
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            if (result.ExitCode != 0)
                return null;

            var secret = result.StandardOutput.TrimEnd('\n', '\r');
            return string.IsNullOrEmpty(secret) ? null : secret;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SetAsync(string key, string secret, CancellationToken ct = default)
    {
        try
        {
            await Cli.Wrap(Executable)
                .WithArguments(new[]
                {
                    "store", "--label", $"Ayn Thor Manager ({key})",
                    "application", AppAttribute, "key", key,
                })
                .WithStandardInputPipe(PipeSource.FromString(secret))
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);
        }
        catch (Exception)
        {
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await Cli.Wrap(Executable)
                .WithArguments(new[] { "clear", "application", AppAttribute, "key", key })
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);
        }
        catch (Exception)
        {
        }
    }
}
