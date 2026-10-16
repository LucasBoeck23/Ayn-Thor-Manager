using AynThorManager.Core.Helpers;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Core.Models.Responses;
using AynThorManager.Core.Validators;
using AynThorManager.Services.Connection;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.FileStorage;

public sealed class FileStorageService(
    DeviceConnection device,
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
        return await RunFileOpAsync(path, command, commandResult =>
        {
            var sorted = LsOutputParser.Parse(commandResult.StandardOutput)
                .OrderBy(e => e.Type == FileEntryType.Directory ? 0 : 1)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var totalCount = sorted.Count;
            var isTruncated = totalCount > MaxEntries;
            var truncated = isTruncated ? sorted.Take(MaxEntries).ToList() : sorted;

            logger.LogInformation("Listed {TotalCount} entries in '{Path}' (truncated: {IsTruncated})", totalCount, path, isTruncated);
            return new ListDirectoryResponse(truncated, path, isTruncated, totalCount);
        }, ct);
    }

    public Task<Result<CreateDirectoryResponse>> CreateDirectoryAsync(string parentPath, string name, CancellationToken ct) =>
        CreateEntryAsync(parentPath, name, "mkdir", "Directory created: {FullPath}",
            fullPath => new CreateDirectoryResponse(fullPath), ct);

    public Task<Result<CreateFileResponse>> CreateFileAsync(string parentPath, string name, CancellationToken ct) =>
        CreateEntryAsync(parentPath, name, "touch", "File created: {FullPath}",
            fullPath => new CreateFileResponse(fullPath), ct);

    private async Task<Result<T>> CreateEntryAsync<T>(
        string parentPath, string name, string shellVerb, string successLog,
        Func<string, T> toResponse, CancellationToken ct)
    {
        var nameValidation = FileNameValidator.Validate(name);
        if (!nameValidation.IsSuccess)
            return Result<T>.Failure(nameValidation.Error!);

        var preCheck = ValidatePathAndConnection(parentPath);
        if (preCheck is not null)
            return Result<T>.Failure(preCheck);

        var fullPathValidation = PathValidator.ValidateFullPathLength(parentPath, name);
        if (!fullPathValidation.IsSuccess)
            return Result<T>.Failure(fullPathValidation.Error!);

        var fullPath = PathHelper.Combine(parentPath, name);
        var command = new AdbCommand($"shell {shellVerb} \"{fullPath}\"", CreateDirectoryTimeout, $"{shellVerb} {fullPath}");
        return await RunFileOpAsync(parentPath, command, _ =>
        {
            logger.LogInformation(successLog, fullPath);
            return toResponse(fullPath);
        }, ct);
    }

    public async Task<Result<UploadFileResponse>> UploadFileAsync(string localFilePath, string destinationPath, CancellationToken ct)
    {
        if (!File.Exists(localFilePath))
            return Result<UploadFileResponse>.Failure(new Error("LOCAL_FILE_NOT_FOUND", "O arquivo local não foi encontrado."));

        var preCheck = ValidatePathAndConnection(destinationPath);
        if (preCheck is not null)
            return Result<UploadFileResponse>.Failure(preCheck);

        var fileName = Path.GetFileName(localFilePath);
        var fullPath = PathHelper.Combine(destinationPath, fileName);

        var timeout = TimeoutForFile(localFilePath);
        var command = new AdbCommand($"push \"{localFilePath}\" \"{fullPath}\"", timeout, $"push {fileName}");
        return await RunFileOpAsync(destinationPath, command, _ =>
        {
            logger.LogInformation("Uploaded '{Local}' to '{FullPath}'", localFilePath, fullPath);
            return new UploadFileResponse(fullPath);
        }, ct);
    }

    private static TimeSpan TimeoutForFile(string localFilePath)
    {
        // Allow ~1 minute per 100 MB (min 1 min), a generous floor for slow links.
        try
        {
            var sizeMb = new FileInfo(localFilePath).Length / (1024.0 * 1024.0);
            var minutes = Math.Max(1, (int)Math.Ceiling(sizeMb / 100.0));
            return TimeSpan.FromMinutes(minutes);
        }
        catch
        {
            return TimeSpan.FromMinutes(5);
        }
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
        return await RunFileOpAsync(currentPath, command, _ =>
        {
            logger.LogInformation("Renamed '{CurrentPath}' to '{NewPath}'", currentPath, newFullPath);
            return new RenameResponse(newFullPath);
        }, ct);
    }

    public async Task<Result<DeleteResponse>> DeleteAsync(string path, CancellationToken ct)
    {
        var preCheck = ValidatePathAndConnection(path);
        if (preCheck is not null)
            return Result<DeleteResponse>.Failure(preCheck);

        var command = new AdbCommand($"shell rm -rf \"{path}\"", DeleteTimeout, $"rm -rf {path}");
        return await RunFileOpAsync(path, command, _ =>
        {
            logger.LogInformation("Deleted: {Path}", path);
            return new DeleteResponse(path);
        }, ct);
    }

    private Error? ValidatePathAndConnection(string path)
    {
        var pathValidation = PathValidator.Validate(path);
        if (!pathValidation.IsSuccess)
        {
            logger.LogWarning("Operation rejected: invalid path '{Path}'", path);
            return pathValidation.Error!;
        }

        if (!device.IsConnected)
        {
            logger.LogWarning("Operation rejected: device not connected");
            return new Error("DEVICE_NOT_CONNECTED", "O Thor não está conectado.");
        }

        return null;
    }

    private async Task<Result<CommandResult>> ExecuteAndParseAsync(AdbCommand command, string contextPath, CancellationToken ct)
    {
        var result = await device.RunAsync(command.Arguments, command.Timeout, ct);

        if (!result.IsSuccess)
        {
            logger.LogWarning("Command failed: {Description} - {Error}", command.Description, result.Error!.Code);
            return Result<CommandResult>.Failure(result.Error);
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
            var detail = (commandResult.StandardError + " " + commandResult.StandardOutput).Trim();
            return Result<CommandResult>.Failure(new Error("DEVICE_OPERATION_FAILED",
                string.IsNullOrEmpty(detail) ? "Falha na operação no Thor." : $"Falha na operação no Thor: {detail}"));
        }

        return Result<CommandResult>.Success(commandResult);
    }

    private async Task<Result<T>> RunFileOpAsync<T>(
        string contextPath, AdbCommand command, Func<CommandResult, T> onSuccess, CancellationToken ct)
    {
        var result = await ExecuteAndParseAsync(command, contextPath, ct);
        return result.IsSuccess
            ? Result<T>.Success(onSuccess(result.Value!))
            : Result<T>.Failure(result.Error!);
    }

}
