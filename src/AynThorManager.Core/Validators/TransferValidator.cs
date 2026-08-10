using AynThorManager.Core.Models;

namespace AynThorManager.Core.Validators;

/// <summary>
/// Validates transfer requests against upload limits.
/// </summary>
public static class TransferValidator
{
    /// <summary>
    /// Maximum number of files allowed per upload operation.
    /// </summary>
    public const int MaxFilesPerUpload = 20;

    /// <summary>
    /// Maximum file size in bytes (4 GB = 4 * 1024 * 1024 * 1024).
    /// </summary>
    public const long MaxFileSizeBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Validates that the total file size does not exceed available space on the device.
    /// </summary>
    /// <param name="totalFileSize">The sum of all file sizes in bytes.</param>
    /// <param name="availableSpace">The available space on the device in bytes.</param>
    /// <returns>A successful result if space is sufficient, or a failure with INSUFFICIENT_SPACE error.</returns>
    public static Result ValidateAvailableSpace(long totalFileSize, long availableSpace)
    {
        if (totalFileSize > availableSpace)
        {
            return Result.Failure(new Error(
                "INSUFFICIENT_SPACE",
                $"Espaço insuficiente no dispositivo. Necessário: {totalFileSize} bytes, disponível: {availableSpace} bytes.",
                new Dictionary<string, object>
                {
                    ["requiredBytes"] = totalFileSize,
                    ["availableBytes"] = availableSpace
                }));
        }

        return Result.Success();
    }

    /// <summary>
    /// Validates that the transfer request does not exceed upload limits.
    /// Rejects if file count exceeds 20 OR if any individual file exceeds 4 GB.
    /// </summary>
    /// <param name="files">The list of files to validate.</param>
    /// <returns>A successful result if within limits, or a failure with FILE_LIMIT_EXCEEDED error.</returns>
    public static Result ValidateFileLimits(IReadOnlyList<TransferFile> files)
    {
        if (files.Count > MaxFilesPerUpload)
        {
            return Result.Failure(new Error(
                "FILE_LIMIT_EXCEEDED",
                $"O número de arquivos ({files.Count}) excede o limite máximo de {MaxFilesPerUpload} arquivos por operação.",
                new Dictionary<string, object>
                {
                    ["limit"] = "file_count",
                    ["max"] = MaxFilesPerUpload,
                    ["actual"] = files.Count
                }));
        }

        var oversizedFile = files.FirstOrDefault(f => f.SizeBytes > MaxFileSizeBytes);
        if (oversizedFile is not null)
        {
            return Result.Failure(new Error(
                "FILE_LIMIT_EXCEEDED",
                $"O arquivo '{oversizedFile.FileName}' ({oversizedFile.SizeBytes} bytes) excede o tamanho máximo de 4 GB por arquivo.",
                new Dictionary<string, object>
                {
                    ["limit"] = "file_size",
                    ["maxBytes"] = MaxFileSizeBytes,
                    ["fileName"] = oversizedFile.FileName,
                    ["actualBytes"] = oversizedFile.SizeBytes
                }));
        }

        return Result.Success();
    }
}
