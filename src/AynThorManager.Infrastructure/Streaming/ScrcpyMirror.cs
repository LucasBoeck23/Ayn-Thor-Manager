using System.Diagnostics;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Streaming;

public sealed class ScrcpyMirror : IScreenMirror
{
    private const string Executable = "scrcpy";

    private readonly object _lock = new();
    private readonly List<Process> _processes = [];

    public bool IsRunning
    {
        get { lock (_lock) return _processes.Any(p => !p.HasExited); }
    }

    public event Action? Exited;

    public void Start(IReadOnlyList<string> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (instances.Count == 0)
            throw new ArgumentException("At least one scrcpy instance is required.", nameof(instances));

        lock (_lock)
        {
            if (_processes.Any(p => !p.HasExited))
                return;

            _processes.Clear();

            foreach (var arguments in instances)
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = Executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Não foi possível iniciar o scrcpy.");

                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;
                _processes.Add(process);
            }
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_processes.Any(p => !p.HasExited))
                return;
        }

        Exited?.Invoke();
    }

    public void Stop()
    {
        List<Process> processes;
        lock (_lock)
        {
            processes = [.. _processes];
            _processes.Clear();
        }

        foreach (var process in processes)
        {
            try
            {
                process.Exited -= OnProcessExited;
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (InvalidOperationException) { }
            finally
            {
                process.Dispose();
            }
        }
    }
}
