using AynThorManager.Api.Middleware;
using AynThorManager.Core.DTOs;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Core.Models.Responses;

namespace AynThorManager.Api.Endpoints;

public static class FileEndpoints
{
    public static RouteGroupBuilder MapFileEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/files").WithTags("Files");

        group.MapGet("/", ListDirectoryAsync);
        group.MapPost("/directory", CreateDirectoryAsync);
        group.MapPut("/rename", RenameAsync);
        group.MapDelete("/", DeleteAsync);
        group.MapPost("/upload", UploadAsync).DisableAntiforgery();
        group.MapPost("/upload/cancel", CancelUploadAsync);

        return group;
    }

    private static async Task<IResult> ListDirectoryAsync(string path, IFileStorageService fs, CancellationToken ct)
    {
        var result = await fs.ListDirectoryAsync(path, ct);
        if (!result.IsSuccess) return ErrorMapping.ToProblemResult(result.Error!);

        var v = result.Value!;
        return TypedResults.Ok(new ListDirectoryResponseDto(
            Entries: v.Entries.Select(e => new FileEntryDto(e.Name, e.SizeBytes, e.ModifiedAt.ToString("o"),
                e.Type == FileEntryType.Directory ? "directory" : "file")).ToList(),
            Path: v.Path, IsTruncated: v.IsTruncated, TotalCount: v.TotalCount));
    }

    private static async Task<IResult> CreateDirectoryAsync(CreateDirectoryRequestDto request, IFileStorageService fs, CancellationToken ct)
    {
        var result = await fs.CreateDirectoryAsync(request.ParentPath, request.Name, ct);
        if (!result.IsSuccess) return ErrorMapping.ToProblemResult(result.Error!);
        return TypedResults.Created($"/api/files?path={Uri.EscapeDataString(result.Value!.FullPath)}", result.Value);
    }

    private static async Task<IResult> RenameAsync(RenameRequestDto request, IFileStorageService fs, CancellationToken ct)
    {
        var result = await fs.RenameAsync(request.CurrentPath, request.NewName, ct);
        if (!result.IsSuccess) return ErrorMapping.ToProblemResult(result.Error!);
        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> DeleteAsync(string path, IFileStorageService fs, CancellationToken ct)
    {
        var result = await fs.DeleteAsync(path, ct);
        if (!result.IsSuccess) return ErrorMapping.ToProblemResult(result.Error!);
        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> UploadAsync(string destination, HttpRequest httpRequest, ITransferService transferService, CancellationToken ct)
    {
        if (!httpRequest.HasFormContentType)
            return ErrorMapping.ToProblemResult(new Error("INVALID_INPUT", "Request must be multipart/form-data."));
        if (transferService.IsTransferInProgress)
            return ErrorMapping.ToProblemResult(new Error("TRANSFER_IN_PROGRESS", "A transfer is already in progress."));

        var form = await httpRequest.ReadFormAsync(ct);
        if (form.Files.Count == 0)
            return ErrorMapping.ToProblemResult(new Error("INVALID_INPUT", "No files provided."));

        var tempDir = Path.Combine(Path.GetTempPath(), "ayn-thor-uploads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var transferFiles = new List<TransferFile>(form.Files.Count);
        foreach (var file in form.Files)
        {
            var tempPath = Path.Combine(tempDir, file.FileName);
            await using var stream = new FileStream(tempPath, FileMode.Create);
            await file.CopyToAsync(stream, ct);
            transferFiles.Add(new TransferFile(tempPath, file.FileName, file.Length));
        }

        _ = Task.Run(async () =>
        {
            try { await transferService.UploadAsync(new TransferRequest(transferFiles, destination), CancellationToken.None); }
            finally { try { Directory.Delete(tempDir, true); } catch { } }
        }, CancellationToken.None);

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> CancelUploadAsync(ITransferService transferService, CancellationToken ct)
    {
        var result = await transferService.CancelCurrentTransferAsync(ct);
        if (!result.IsSuccess) return ErrorMapping.ToProblemResult(result.Error!);
        return TypedResults.Ok();
    }
}
