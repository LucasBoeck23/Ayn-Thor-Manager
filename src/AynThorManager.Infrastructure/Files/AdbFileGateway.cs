using AynThorManager.Core.Helpers;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Files;

public sealed class AdbFileGateway : IAdbFileGateway
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(10);

    private readonly IAdbCommandExecutor _executor;
    private readonly string _serial;

    public AdbFileGateway(IAdbCommandExecutor executor, string serial)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A serial/endereço ADB do Thor é obrigatória.", nameof(serial));

        _executor = executor;
        _serial = serial;
    }

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

    public async Task<Result<ThorFileEntry>> StatAsync(string remotePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return Result<ThorFileEntry>.Failure(InvalidPath());

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

        var name = LeafName(remotePath);
        return Result<ThorFileEntry>.Success(entry with { Name = name });
    }

    public async Task<Result> PullAsync(string remotePath, string localPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath) || string.IsNullOrWhiteSpace(localPath))
            return Result.Failure(InvalidPath());

        var result = await _executor.ExecuteAsync(
            new[] { "-s", _serial, "pull", remotePath, localPath }, TransferTimeout, ct);

        return result.Success
            ? Result.Success()
            : Result.Failure(MapError(result, remotePath));
    }

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

    public async Task<Result> DeleteAsync(string remotePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return Result.Failure(InvalidPath());

        var result = await _executor.ExecuteAsync(
            $"-s {_serial} shell rm -rf {Quote(remotePath)}", MetadataTimeout, ct);

        return result.Success
            ? Result.Success()
            : Result.Failure(MapError(result, remotePath));
    }

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

    private static string Quote(string path)
    {
        var escaped = path.Replace("'", "'\\''");
        return $"'{escaped}'";
    }
}
