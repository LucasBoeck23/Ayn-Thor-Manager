using System.Diagnostics;
using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Files;

/// <summary>
/// Opens the native file manager at a local folder path. Primary path is
/// <c>xdg-open &lt;path&gt;</c> (the freedesktop way to hand a path to the user's
/// configured file manager); when that is unavailable or fails it falls back to a
/// shell-executed process (<see cref="ProcessStartInfo.UseShellExecute"/>), mirroring the
/// "open URL/file in the OS" pattern used by the reverse-streaming feature. Best-effort:
/// this class only reveals the mount point and never navigates files itself (Req 1).
///
/// Command execution goes through a small injectable seam (<see cref="CommandRunner"/>),
/// matching the convention of <c>SunshinePrerequisites</c>, so the launch can be driven
/// with fake command outcomes without spawning anything real. The default constructor
/// wires the real CliWrap runner.
/// </summary>
public sealed class FileManagerLauncher : IFileManagerLauncher
{
    private readonly CommandRunner _run;

    /// <summary>Launches via the real system through CliWrap.</summary>
    public FileManagerLauncher() : this(RealRun)
    {
    }

    /// <summary>
    /// Test seam: injects how the launch command is executed, so the behaviour can be
    /// driven with a fake outcome without touching the real system.
    /// </summary>
    internal FileManagerLauncher(CommandRunner run) => _run = run;

    /// <summary>
    /// Opens the native file manager at <paramref name="path"/>. Tries
    /// <c>xdg-open &lt;path&gt;</c> first; if that cannot be launched or exits non-zero,
    /// falls back to a shell-executed process on the same path. Throws only when neither
    /// route can hand the path to the OS.
    /// </summary>
    public async Task OpenAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A mount path is required to open the file manager.", nameof(path));

        // Primary: xdg-open hands the path to the user's configured file manager.
        var result = await _run("xdg-open", new[] { path }, ct);
        if (result.ExitCode == 0)
            return;

        // Fallback: let the OS shell resolve the folder (Avalonia-launcher-style open by
        // path). UseShellExecute mirrors the reverse-streaming "open in the OS" helper.
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

    /// <summary>Runs a command via CliWrap, mirroring the helper in <c>SunshinePrerequisites</c>.</summary>
    private static async Task<CommandOutput> RealRun(string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            var result = await Cli.Wrap(file)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            return new CommandOutput(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (Exception ex)
        {
            return new CommandOutput(-1, string.Empty, ex.Message);
        }
    }
}

/// <summary>
/// Executes a shell command and returns its outcome. The real implementation wraps
/// CliWrap; tests supply a fake to feed a canned outcome into the launch. Kept local to
/// the file-manager launcher so it does not couple to the streaming helpers' seam.
/// </summary>
internal delegate Task<CommandOutput> CommandRunner(
    string file, IReadOnlyList<string> args, CancellationToken ct);

/// <summary>Minimal result of a shell command: exit code and captured output streams.</summary>
internal readonly record struct CommandOutput(
    int ExitCode,
    string StandardOutput,
    string StandardError);
