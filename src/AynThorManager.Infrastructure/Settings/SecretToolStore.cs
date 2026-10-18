using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Settings;

/// <summary>
/// Stores secrets in the system keyring via <c>secret-tool</c> (libsecret / Secret
/// Service — the GNOME keyring backend). Each secret is tagged with the app name and a
/// key attribute so entries are namespaced and overwrite cleanly.
///
/// Best-effort by design (<see cref="ISecretStore"/>): if <c>secret-tool</c> or a
/// keyring backend isn't available, <see cref="GetAsync"/> returns null and
/// set/delete are no-ops — the app keeps working, it just won't remember the password.
/// The secret value is passed to <c>secret-tool store</c> on stdin, so it never appears
/// in the process arguments / command line.
/// </summary>
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

            // secret-tool prints the secret with no trailing newline; be tolerant anyway.
            var secret = result.StandardOutput.TrimEnd('\n', '\r');
            return string.IsNullOrEmpty(secret) ? null : secret;
        }
        catch (Exception)
        {
            return null; // no secret-tool / no keyring backend
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
                // Pass the secret on stdin so it never shows up in the command line.
                .WithStandardInputPipe(PipeSource.FromString(secret))
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);
        }
        catch (Exception)
        {
            // Best-effort: without a keyring we simply don't remember the secret.
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
            // Best-effort.
        }
    }
}
