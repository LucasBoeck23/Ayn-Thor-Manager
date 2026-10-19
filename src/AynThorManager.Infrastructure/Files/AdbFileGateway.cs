using AynThorManager.Core.Helpers;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Files;

/// <summary>
/// Infrastructure implementation of <see cref="IAdbFileGateway"/> on top of
/// <see cref="IAdbCommandExecutor"/>. Every operation runs against the active device by
/// prefixing <c>-s {serial}</c> (Req 5.3), so the FUSE translator never commands the
/// wrong target and never speaks raw ADB itself.
///
/// Listings come from <c>ls -la</c> parsed by <see cref="LsOutputParser"/> into
/// <see cref="ThorFileEntry"/> (names/metadata only, no content — the lazy path of
/// Req 2.1); content moves per-file via <c>pull</c>/<c>push</c>; and
/// <c>rm</c>/<c>mkdir</c>/<c>mv</c> back the remaining operations. ADB failures are mapped
/// to a clear <see cref="Result"/> failure via <see cref="AdbErrorParser"/> rather than
/// thrown, so the FUSE callbacks can turn them into <c>-errno</c> instead of crashing the
/// mount.
/// </summary>
public sealed class AdbFileGateway : IAdbFileGateway
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(10);

    private readonly IAdbCommandExecutor _executor;
    private readonly string _serial;

    /// <summary>
    /// Builds a gateway bound to one active device. The Services orchestrator (which owns
    /// the <c>DeviceConnection</c>) passes the serial of the active <c>device</c> entry so
    /// every command here targets exactly that Thor (Req 5.1/5.3).
    /// </summary>
    public AdbFileGateway(IAdbCommandExecutor executor, string serial)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A serial/endereço ADB do Thor é obrigatória.", nameof(serial));

        _executor = executor;
        _serial = serial;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ThorFileEntry>>> ListAsync(string remotePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return Result<IReadOnlyList<ThorFileEntry>>.Failure(InvalidPath());

        var result = await _executor.ExecuteAsync(
            $"-s {_serial} shell ls -la {Quote(remotePath)}", ListTimeout, ct);

        if (!result.Success)
            return Result<IReadOnlyList<ThorFileEntry>>.Failure(MapError(result, remotePath));

        var entries = LsOutputParser.Parse(result.StandardOutput);
        return Result<IReadOnlyList<ThorFileEntry>>.Success(entries);
    }

    /// <inheritdoc />
    public async Task<Result<ThorFileEntry>> StatAsync(string remotePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return Result<ThorFileEntry>.Failure(InvalidPath());

        // `ls -lad` on the node itself lists exactly one line describing the node (for a
        // directory, the directory entry rather than its children), which is what a stat
        // of a single node needs.
        var result = await _executor.ExecuteAsync(
            $"-s {_serial} shell ls -lad {Quote(remotePath)}", MetadataTimeout, ct);

        if (!result.Success)
            return Result<ThorFileEntry>.Failure(MapError(result, remotePath));

        var line = result.StandardOutput
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .FirstOrDefault(l => l.Trim().Length > 0 && !l.StartsWith("total ", StringComparison.Ordinal));

        var entry = line is null ? null : LsOutputParser.ParseLine(line);
        if (entry is null)
            return Result<ThorFileEntry>.Failure(new Error("STAT_FAILED", $"Não foi possível ler os dados de {remotePath}."));

        // `ls -lad` reports the node's own name (often the full path for a directory);
        // keep only the leaf so callers get a stable name.
        var name = LeafName(remotePath);
        return Result<ThorFileEntry>.Success(entry with { Name = name });
    }

    /// <inheritdoc />
    public async Task<Result> PullAsync(string remotePath, string localPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath) || string.IsNullOrWhiteSpace(localPath))
            return Result.Failure(InvalidPath());

        // push/pull take literal local/remote paths — adb does NOT run them through a
        // device shell, so shell-style quoting would be taken literally and corrupt the
        // path. Pass each operand as its own argv entry instead.
        var result = await _executor.ExecuteAsync(
            new[] { "-s", _serial, "pull", remotePath, localPath }, TransferTimeout, ct);

        return result.Success
            ? Result.Success()
            : Result.Failure(MapError(result, remotePath));
    }

    /// <inheritdoc />
    public async Task<Result> PushAsync(string localPath, string remotePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(remotePath))
            return Result.Failure(InvalidPath());

        var result = await _executor.ExecuteAsync(
            new[] { "-s", _serial, "push", localPath, remotePath }, TransferTimeout, ct);

        return result.Success
            ? Result.Success()
            : Result.Failure(MapError(result, remotePath));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(string remotePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return Result.Failure(InvalidPath());

        // -rf so removing a non-empty directory succeeds; a missing target still reports a
        // clear PATH_NOT_FOUND via the error parser.
        var result = await _executor.ExecuteAsync(
            $"-s {_serial} shell rm -rf {Quote(remotePath)}", MetadataTimeout, ct);

        return result.Success
            ? Result.Success()
            : Result.Failure(MapError(result, remotePath));
    }

    /// <inheritdoc />
    public async Task<Result> MakeDirAsync(string remotePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return Result.Failure(InvalidPath());

        var result = await _executor.ExecuteAsync(
            $"-s {_serial} shell mkdir {Quote(remotePath)}", MetadataTimeout, ct);

        return result.Success
            ? Result.Success()
            : Result.Failure(MapError(result, remotePath));
    }

    /// <inheritdoc />
    public async Task<Result> RenameAsync(string fromRemotePath, string toRemotePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fromRemotePath) || string.IsNullOrWhiteSpace(toRemotePath))
            return Result.Failure(InvalidPath());

        var result = await _executor.ExecuteAsync(
            $"-s {_serial} shell mv {Quote(fromRemotePath)} {Quote(toRemotePath)}", MetadataTimeout, ct);

        return result.Success
            ? Result.Success()
            : Result.Failure(MapError(result, fromRemotePath));
    }

    /// <summary>
    /// Maps a failed ADB command to a clear <see cref="Error"/>: recognized shell errors
    /// (not found, permission denied, name exists) come from <see cref="AdbErrorParser"/>;
    /// anything else falls back to a generic ADB failure carrying the raw stderr/stdout so
    /// the cause is never silently lost.
    /// </summary>
    private static Error MapError(CommandResult result, string contextPath)
    {
        var parsed = AdbErrorParser.TryParseError(result.StandardOutput, result.StandardError, contextPath);
        if (parsed is not null)
            return parsed;

        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return new Error("ADB_COMMAND_FAILED",
            string.IsNullOrWhiteSpace(detail)
                ? "O comando ADB falhou."
                : $"O comando ADB falhou: {detail.Trim()}");
    }

    private static Error InvalidPath() =>
        new("INVALID_PATH", "O caminho informado é inválido.");

    private static string LeafName(string remotePath)
    {
        var trimmed = remotePath.TrimEnd('/');
        if (trimmed.Length == 0)
            return "/";

        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }

    /// <summary>
    /// Single-quotes a path for the remote shell so spaces and glob characters are passed
    /// literally. Embedded single quotes are escaped the POSIX way
    /// (<c>'\''</c>). adb forwards the argument string to <c>sh -c</c> on the device, so
    /// unquoted spaces would otherwise split one path into several arguments.
    /// </summary>
    private static string Quote(string path)
    {
        var escaped = path.Replace("'", "'\\''");
        return $"'{escaped}'";
    }
}
