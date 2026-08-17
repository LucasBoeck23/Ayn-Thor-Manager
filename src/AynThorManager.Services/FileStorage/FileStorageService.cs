using System.Globalization;
using AynThorManager.Core.Helpers;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Core.Models.Responses;
using AynThorManager.Core.Validators;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.FileStorage;

public sealed class FileStorageService(
    ICommandQueue commandQueue,
    IAdbConnectionManager connectionManager,
    ILogger<FileStorageService> logger) : IFileStorageService
{
    private static readonly TimeSpan ListDirectoryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CreateDirectoryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RenameTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DeleteTimeout = TimeSpan.FromSeconds(30);

    private const int MaxEntries = 1000;

    public async Task<Result<ListDirectoryResponse>> ListDirectoryAsync(string path, CancellationToken ct)
    {
        var preCheck = ValidatePathAndConnection(path);
        if (preCheck is not null)
            return Result<ListDirectoryResponse>.Failure(preCheck);

        var command = new AdbCommand($"shell ls -la {path}", ListDirectoryTimeout, $"ls -la {path}");
        var result = await ExecuteAndParseAsync(command, path, ct);

        if (!result.IsSuccess)
            return Result<ListDirectoryResponse>.Failure(result.Error!);

        var commandResult = result.Value!;

        var entries = ParseLsOutput(commandResult.StandardOutput);

        var sorted = entries
            .OrderBy(e => e.Type == FileEntryType.Directory ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalCount = sorted.Count;
        var isTruncated = totalCount > MaxEntries;
        var truncated = isTruncated ? sorted.Take(MaxEntries).ToList() : sorted;

        logger.LogInformation("Listed {TotalCount} entries in '{Path}' (truncated: {IsTruncated})", totalCount, path, isTruncated);
        return Result<ListDirectoryResponse>.Success(new ListDirectoryResponse(truncated, path, isTruncated, totalCount));
    }

    public async Task<Result<CreateDirectoryResponse>> CreateDirectoryAsync(string parentPath, string name, CancellationToken ct)
    {
        var nameValidation = FileNameValidator.Validate(name);
        if (!nameValidation.IsSuccess)
            return Result<CreateDirectoryResponse>.Failure(nameValidation.Error!);

        var preCheck = ValidatePathAndConnection(parentPath);
        if (preCheck is not null)
            return Result<CreateDirectoryResponse>.Failure(preCheck);

        var fullPathValidation = PathValidator.ValidateFullPathLength(parentPath, name);
        if (!fullPathValidation.IsSuccess)
            return Result<CreateDirectoryResponse>.Failure(fullPathValidation.Error!);

        var fullPath = PathHelper.Combine(parentPath, name);
        var command = new AdbCommand($"shell mkdir \"{fullPath}\"", CreateDirectoryTimeout, $"mkdir {fullPath}");
        var result = await ExecuteAndParseAsync(command, parentPath, ct);

        if (!result.IsSuccess)
            return Result<CreateDirectoryResponse>.Failure(result.Error!);

        logger.LogInformation("Directory created: {FullPath}", fullPath);
        return Result<CreateDirectoryResponse>.Success(new CreateDirectoryResponse(fullPath));
    }

    public async Task<Result<RenameResponse>> RenameAsync(string currentPath, string newName, CancellationToken ct)
    {
        var nameValidation = FileNameValidator.Validate(newName);
        if (!nameValidation.IsSuccess)
            return Result<RenameResponse>.Failure(nameValidation.Error!);

        var preCheck = ValidatePathAndConnection(currentPath);
        if (preCheck is not null)
            return Result<RenameResponse>.Failure(preCheck);

        var parentPath = PathHelper.GetParentPath(currentPath);
        var newFullPath = PathHelper.Combine(parentPath, newName);

        var command = new AdbCommand($"shell mv \"{currentPath}\" \"{newFullPath}\"", RenameTimeout, "rename");
        var result = await ExecuteAndParseAsync(command, currentPath, ct);

        if (!result.IsSuccess)
            return Result<RenameResponse>.Failure(result.Error!);

        logger.LogInformation("Renamed '{CurrentPath}' to '{NewPath}'", currentPath, newFullPath);
        return Result<RenameResponse>.Success(new RenameResponse(newFullPath));
    }

    public async Task<Result<DeleteResponse>> DeleteAsync(string path, CancellationToken ct)
    {
        var preCheck = ValidatePathAndConnection(path);
        if (preCheck is not null)
            return Result<DeleteResponse>.Failure(preCheck);

        var command = new AdbCommand($"shell rm -rf \"{path}\"", DeleteTimeout, $"rm -rf {path}");
        var result = await ExecuteAndParseAsync(command, path, ct);

        if (!result.IsSuccess)
            return Result<DeleteResponse>.Failure(result.Error!);

        logger.LogInformation("Deleted: {Path}", path);
        return Result<DeleteResponse>.Success(new DeleteResponse(path));
    }

    private Error? ValidatePathAndConnection(string path)
    {
        var pathValidation = PathValidator.Validate(path);
        if (!pathValidation.IsSuccess)
        {
            logger.LogWarning("Operation rejected: invalid path '{Path}'", path);
            return pathValidation.Error!;
        }

        if (!connectionManager.IsConnected)
        {
            logger.LogWarning("Operation rejected: device not connected");
            return new Error("DEVICE_NOT_CONNECTED", "O dispositivo nÃ£o estÃ¡ conectado via ADB.");
        }

        return null;
    }

    private async Task<Result<CommandResult>> ExecuteAndParseAsync(AdbCommand command, string contextPath, CancellationToken ct)
    {
        var result = await commandQueue.EnqueueAsync(command, CommandPriority.Normal, ct);

        if (!result.IsSuccess)
        {
            logger.LogWarning("Command failed: {Description} â€” {Error}", command.Description, result.Error!.Code);
            return Result<CommandResult>.Failure(new Error("TIMEOUT", "O dispositivo nÃ£o respondeu dentro do tempo limite."));
        }

        var commandResult = result.Value!;

        var parsedError = AdbErrorParser.TryParseError(commandResult.StandardOutput, commandResult.StandardError, contextPath);
        if (parsedError is not null)
        {
            logger.LogWarning("ADB error for '{Path}': {Code}", contextPath, parsedError.Code);
            return Result<CommandResult>.Failure(parsedError);
        }

        if (!commandResult.Success)
        {
            logger.LogWarning("Command failed with exit code {ExitCode}: {Stderr}", commandResult.ExitCode, commandResult.StandardError);
            return Result<CommandResult>.Failure(new Error("DEVICE_OPERATION_FAILED", "Falha na operaÃ§Ã£o no dispositivo."));
        }

        return Result<CommandResult>.Success(commandResult);
    }

    private static List<FileEntry> ParseLsOutput(string output)
    {
        var entries = new List<FileEntry>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var entry = TryParseLsLine(line);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    private static FileEntry? TryParseLsLine(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.Length < 10 || trimmed.StartsWith("total", StringComparison.OrdinalIgnoreCase))
            return null;

        var typeChar = trimmed[0];
        if (typeChar is not ('d' or '-' or 'l'))
            return null;

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 8)
            return null;

        var name = string.Join(' ', parts[7..]);

        if (name is "." or "..")
            return null;

        if (typeChar == 'l')
        {
            var arrowIndex = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
                name = name[..arrowIndex];
        }

        var entryType = typeChar == 'd' ? FileEntryType.Directory : FileEntryType.File;
        long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);

        var dateTimeStr = $"{parts[5]} {parts[6]}";
        if (!DateTimeOffset.TryParseExact(dateTimeStr, "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var modifiedAt))
        {
            modifiedAt = DateTimeOffset.MinValue;
        }

        return new FileEntry(name, size, modifiedAt, entryType);
    }
}
