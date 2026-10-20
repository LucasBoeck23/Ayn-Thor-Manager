using System.Diagnostics;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Streaming;

public sealed class ScrcpyMirror : IScreenMirror
{
    private const string Executable = "scrcpy";

    private readonly object _lock = new();
    private Process? _process;

    public bool IsRunning
    {
        get { lock (_lock) return _process is { HasExited: false }; }
    }

    public event Action? Exited;

    public void Start(string arguments)
    {
        lock (_lock)
        {
            if (_process is { HasExited: false })
                return;

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = Executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Não foi possível iniciar o scrcpy.");

            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;
            _process = process;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e) => Exited?.Invoke();

    public void Stop()
    {
        Process? process;
        lock (_lock)
        {
            process = _process;
            _process = null;
        }

        if (process is null)
            return;

        try
        {
            process.Exited -= OnProcessExited;
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (InvalidOperationException) {  }
        finally
        {
            process.Dispose();
        }
    }
}
