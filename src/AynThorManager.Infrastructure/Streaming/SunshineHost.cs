using System.Diagnostics;
using System.Net.Sockets;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Streaming;

/// <summary>
/// Runs Sunshine as a child process. Sunshine is the PC-side game-streaming server;
/// this class only launches, tracks and kills it. Structural clone of
/// <see cref="ScrcpyMirror"/>, with a tiny launcher seam so the process lifecycle
/// (started / running / exited) can be faked in tests.
/// </summary>
public sealed class SunshineHost : IGameStreamHost
{
    private const string Executable = "sunshine";

    /// <summary>Sunshine's Web UI port — a live instance keeps it listening.</summary>
    private const int WebUiPort = 47990;

    private readonly object _lock = new();
    private readonly Func<ProcessStartInfo, IHostProcess?> _launcher;
    private readonly Func<bool> _isHostUp;
    private IHostProcess? _process;

    /// <summary>Launches the real <c>sunshine</c> binary.</summary>
    public SunshineHost() : this(RealProcess.Start, IsWebUiListening)
    {
    }

    /// <summary>
    /// Test seam: injects how the host process is launched. The delegate must start
    /// the process and return a handle, or null if it could not be started. The host-up
    /// probe defaults to checking the Web UI port.
    /// </summary>
    internal SunshineHost(Func<ProcessStartInfo, IHostProcess?> launcher)
        : this(launcher, IsWebUiListening)
    {
    }

    /// <summary>
    /// Test seam with an injectable "is the host actually up?" probe. Sunshine re-execs
    /// on launch: the process we start exits within seconds while the real daemon keeps
    /// running under a new PID (and keeps the Web UI port open). So when the launched
    /// process exits we consult this probe — if the port is still up, Sunshine is alive
    /// and we must NOT report it as stopped (that was the spurious "Sunshine abortou").
    /// </summary>
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

            // The launched PID may have re-exec'd and exited while the real Sunshine
            // daemon keeps running; the Web UI port is the source of truth for "up".
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
        // Sunshine re-execs: the process we launched exits almost immediately while the
        // real daemon lives on under another PID. Only treat this as a real exit (and
        // raise Exited) when the Web UI port is actually down — otherwise it's the
        // harmless re-exec and reporting "encerrado" would be the spurious abort bug.
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
        catch (InvalidOperationException) { /* already gone */ }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Best-effort probe: can we open a TCP connection to the Sunshine Web UI port on
    /// localhost? A quick connect means the daemon is up. Never throws.
    /// </summary>
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

/// <summary>
/// Minimal view of the process operations <see cref="SunshineHost"/> needs. The real
/// implementation wraps <see cref="Process"/>; tests supply a fake to drive the
/// started / running / exited lifecycle without spawning a real process.
/// </summary>
internal interface IHostProcess : IDisposable
{
    bool HasExited { get; }
    bool EnableRaisingEvents { get; set; }
    event EventHandler Exited;
    void Kill(bool entireProcessTree);
}

/// <summary>Wraps a real <see cref="Process"/> as an <see cref="IHostProcess"/>.</summary>
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
