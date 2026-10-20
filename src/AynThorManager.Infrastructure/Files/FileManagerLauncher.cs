using System.Diagnostics;
using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Processes;

namespace AynThorManager.Infrastructure.Files;

public sealed class FileManagerLauncher : IFileManagerLauncher
{
    private readonly CommandRunner _run;

    public FileManagerLauncher() : this(ProcessRunner.RunAsync)
    {
    }

    internal FileManagerLauncher(CommandRunner run) => _run = run;

    public async Task OpenAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A mount path is required to open the file manager.", nameof(path));

        var result = await _run("xdg-open", new[] { path }, ct);
        if (result.ExitCode == 0)
            return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });

            if (process is not null)
                return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not open the file manager at '{path}'.", ex);
        }

        throw new InvalidOperationException(
            $"Could not open the file manager at '{path}'. " +
            (string.IsNullOrWhiteSpace(result.StandardError) ? null : result.StandardError));
    }
}
