using System.Diagnostics;
using System.Net.Sockets;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Streaming;

public sealed class SunshineHost : IGameStreamHost
{
    private const string Executable = "sunshine";

    private const int WebUiPort = 47990;

    private readonly object _lock = new();
    private readonly Func<ProcessStartInfo, IHostProcess?> _launcher;
    private readonly Func<bool> _isHostUp;
    private IHostProcess? _process;

    public SunshineHost() : this(RealProcess.Start, IsWebUiListening)
    {
    }

    internal SunshineHost(Func<ProcessStartInfo, IHostProcess?> launcher)
        : this(launcher, IsWebUiListening)
    {
    }

    internal SunshineHost(Func<ProcessStartInfo, IHostProcess?> launcher, Func<bool> isHostUp)
    {
        _launcher = launcher;
        _isHostUp = isHostUp;
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                if (_process is { HasExited: false })
                    return true;
            }

            return _isHostUp();
        }
    }

    public event Action? Exited;

    public void Start(string arguments)
    {
        lock (_lock)
        {
            if (_process is { HasExited: false })
                return;

            var process = _launcher(new ProcessStartInfo
            {
                FileName = Executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Não foi possível iniciar o Sunshine.");

            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;
            _process = process;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_isHostUp())
            return;

        Exited?.Invoke();
    }

    public void Stop()
    {
        IHostProcess? process;
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

    private static bool IsWebUiListening()
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync("127.0.0.1", WebUiPort);
            return connect.Wait(TimeSpan.FromMilliseconds(300)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}

internal interface IHostProcess : IDisposable
{
    bool HasExited { get; }
    bool EnableRaisingEvents { get; set; }
    event EventHandler Exited;
    void Kill(bool entireProcessTree);
}

internal sealed class RealProcess(Process process) : IHostProcess
{
    private readonly Process _process = process;

    public static IHostProcess? Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo);
        return process is null ? null : new RealProcess(process);
    }

    public bool HasExited => _process.HasExited;

    public bool EnableRaisingEvents
    {
        get => _process.EnableRaisingEvents;
        set => _process.EnableRaisingEvents = value;
    }

    public event EventHandler Exited
    {
        add => _process.Exited += value;
        remove => _process.Exited -= value;
    }

    public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

    public void Dispose() => _process.Dispose();
}
