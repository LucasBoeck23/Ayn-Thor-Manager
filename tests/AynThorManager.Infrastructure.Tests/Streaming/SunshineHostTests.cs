using AynThorManager.Infrastructure.Streaming;

namespace AynThorManager.Infrastructure.Tests.Streaming;

/// <summary>
/// Lifecycle of the Sunshine child process (start / running / exited / stop),
/// driven through the launcher seam with a fake process so nothing real spawns.
/// </summary>
public class SunshineHostTests
{
    private readonly List<FakeHostProcess> _launched = [];
    private int _launchCount;
    private Func<FakeHostProcess?> _next;
    // Default: the Web UI port reads as DOWN, so the launched-process lifecycle
    // (exit/stop/running) behaves exactly as before. Re-exec tests flip this true.
    private bool _hostUp;

    public SunshineHostTests() => _next = () =>
    {
        var process = new FakeHostProcess();
        _launched.Add(process);
        return process;
    };

    private SunshineHost CreateHost() => new(
        _ =>
        {
            _launchCount++;
            return _next();
        },
        isHostUp: () => _hostUp);

    [Fact]
    public void Start_launches_and_marks_running()
    {
        var host = CreateHost();

        host.Start("--whatever");

        Assert.True(host.IsRunning);
        Assert.Equal(1, _launchCount);
    }

    [Fact]
    public void Start_sets_up_the_process_for_exit_notifications()
    {
        var host = CreateHost();

        host.Start("");

        var process = Assert.Single(_launched);
        Assert.True(process.EnableRaisingEvents);
        Assert.Equal(1, process.ExitedSubscriberCount);
    }

    [Fact]
    public void Start_while_running_does_not_launch_a_second_instance()
    {
        // Req 3.2: Sunshine já rodando → não iniciar uma segunda instância.
        var host = CreateHost();
        host.Start("");

        host.Start("");

        Assert.Equal(1, _launchCount);
        Assert.True(host.IsRunning);
    }

    [Fact]
    public void Exited_event_fires_and_running_reflects_the_exit()
    {
        // Req 3.3: processo termina/falha → refletir estado inativo.
        var host = CreateHost();
        var exitedRaised = 0;
        host.Exited += () => exitedRaised++;
        host.Start("");

        _launched[0].SimulateExit();

        Assert.Equal(1, exitedRaised);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void Start_after_the_process_exited_launches_a_fresh_instance()
    {
        var host = CreateHost();
        host.Start("");
        _launched[0].SimulateExit();

        host.Start("");

        Assert.Equal(2, _launchCount);
        Assert.True(host.IsRunning);
    }

    [Fact]
    public void Stop_kills_the_running_process_and_cleans_up()
    {
        // Req 3.4: parar o Sunshine que ele iniciou (matar o processo).
        var host = CreateHost();
        host.Start("");
        var process = _launched[0];

        host.Stop();

        Assert.Equal(1, process.KillCount);
        Assert.True(process.KilledEntireProcessTree);
        Assert.True(process.Disposed);
        Assert.Equal(0, process.ExitedSubscriberCount);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void Stop_does_not_kill_a_process_that_already_exited()
    {
        var host = CreateHost();
        host.Start("");
        var process = _launched[0];
        process.SimulateExit();

        host.Stop();

        Assert.Equal(0, process.KillCount);
        Assert.True(process.Disposed);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void Stop_when_idle_is_safe()
    {
        var host = CreateHost();

        host.Stop();

        Assert.False(host.IsRunning);
        Assert.Equal(0, _launchCount);
    }

    [Fact]
    public void Start_throws_when_the_binary_cannot_be_launched()
    {
        // Req 3.1: launcher devolve null (não deu para iniciar) → erro claro.
        _next = () => null;
        var host = CreateHost();

        Assert.Throws<InvalidOperationException>(() => host.Start(""));
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void Re_exec_is_not_reported_as_exit_when_the_web_ui_port_is_up()
    {
        // The real bug: Sunshine re-execs, so the PID we launched exits within seconds
        // while the daemon keeps running (Web UI port stays up). That must NOT surface as
        // "Sunshine encerrado", and IsRunning must stay true.
        var host = CreateHost();
        var exitedRaised = 0;
        host.Exited += () => exitedRaised++;
        host.Start("");
        _hostUp = true; // daemon alive on the port despite the launched PID leaving

        _launched[0].SimulateExit();

        Assert.Equal(0, exitedRaised);
        Assert.True(host.IsRunning);
    }

    [Fact]
    public void Real_exit_is_reported_when_the_port_is_down()
    {
        // A genuine crash/stop: the launched process exits AND the port is down → the
        // daemon is really gone, so Exited fires and IsRunning is false.
        var host = CreateHost();
        var exitedRaised = 0;
        host.Exited += () => exitedRaised++;
        host.Start("");
        _hostUp = false;

        _launched[0].SimulateExit();

        Assert.Equal(1, exitedRaised);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void Is_running_is_true_from_the_port_even_without_a_tracked_process()
    {
        // Sunshine may already be running (started by the user/systemd) before we launch.
        // With no tracked process, IsRunning still reflects the port being up.
        _hostUp = true;
        var host = CreateHost();

        Assert.True(host.IsRunning);
    }
}
