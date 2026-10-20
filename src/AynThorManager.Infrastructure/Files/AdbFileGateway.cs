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

        return Result<ThorFileEntry>.Success(entry with { Name = LeafName(remotePath) });
    }

    public Task<Result> PullAsync(string remotePath, string localPath, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(remotePath) || string.IsNullOrWhiteSpace(localPath)
            ? Task.FromResult(Result.Failure(InvalidPath()))
            : RunAsync(new[] { "-s", _serial, "pull", remotePath, localPath }, TransferTimeout, remotePath, ct);

    public Task<Result> PushAsync(string localPath, string remotePath, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(remotePath)
            ? Task.FromResult(Result.Failure(InvalidPath()))
            : RunAsync(new[] { "-s", _serial, "push", localPath, remotePath }, TransferTimeout, remotePath, ct);

    public Task<Result> DeleteAsync(string remotePath, CancellationToken ct = default) =>
        ShellAsync($"rm -rf {Quote(remotePath)}", remotePath, ct);

    public Task<Result> MakeDirAsync(string remotePath, CancellationToken ct = default) =>
        ShellAsync($"mkdir {Quote(remotePath)}", remotePath, ct);

    public Task<Result> RenameAsync(string fromRemotePath, string toRemotePath, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(fromRemotePath) || string.IsNullOrWhiteSpace(toRemotePath)
            ? Task.FromResult(Result.Failure(InvalidPath()))
            : ShellAsync($"mv {Quote(fromRemotePath)} {Quote(toRemotePath)}", fromRemotePath, ct);

    private Task<Result> ShellAsync(string shellCommand, string contextPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
            return Task.FromResult(Result.Failure(InvalidPath()));

        return RunStringAsync($"-s {_serial} shell {shellCommand}", MetadataTimeout, contextPath, ct);
    }

    private async Task<Result> RunStringAsync(string arguments, TimeSpan timeout, string contextPath, CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync(arguments, timeout, ct);
        return result.Success ? Result.Success() : Result.Failure(MapError(result, contextPath));
    }

    private async Task<Result> RunAsync(IReadOnlyList<string> args, TimeSpan timeout, string contextPath, CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync(args, timeout, ct);
        return result.Success ? Result.Success() : Result.Failure(MapError(result, contextPath));
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
