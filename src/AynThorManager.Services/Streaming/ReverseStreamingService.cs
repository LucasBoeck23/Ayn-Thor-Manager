using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Streaming;

/// <summary>
/// Orchestrates reverse streaming (PC → Thor): Sunshine on the PC + Moonlight on the
/// Thor. Mirrors the structure of <see cref="StreamingService"/> but drives the inverse
/// direction. The app is an orchestrator, not an installer — this service only verifies,
/// guides, and launches; it never installs software and never asks for sudo (Req 2).
///
/// This stage implements the prerequisite-validation entry point plus the mutual-exclusion
/// seam. Later work extends the same class with the happy path (start/stop) and fallbacks,
/// so the collaborators are taken in the constructor now to keep it stable:
/// <list type="bullet">
///   <item><description><paramref name="device"/>: reverse streaming is unavailable when no Thor is connected (Req 1.5).</description></item>
///   <item><description><paramref name="forward"/>: the forward direction (Thor→PC, scrcpy); the reverse orchestrator ends it before starting, so the two directions never run at once (Req 7).</description></item>
///   <item><description><paramref name="prerequisites"/>: PC-side checks (Sunshine/encoder/Wayland), via a Core abstraction so Services never references Infrastructure.</description></item>
///   <item><description><paramref name="moonlight"/>: ADB check/launch of Moonlight on the Thor (Req 2.2/2.4, Req 4).</description></item>
///   <item><description><paramref name="host"/>: the Sunshine host process, started/stopped by the happy path (Req 3).</description></item>
/// </list>
/// </summary>
public sealed class ReverseStreamingService
{
    /// <summary>Official Moonlight listing, shown as guidance when it is missing on the Thor.</summary>
    private const string MoonlightStoreUrl =
        "https://play.google.com/store/apps/details?id=com.limelight";

    /// <summary>
    /// Req 9.2: how many times to try opening Moonlight before giving up. The ADB launch
    /// can be flaky right after the hotspot comes up, so a couple of extra attempts turn
    /// a transient failure into a success without bothering the user.
    /// </summary>
    private const int MoonlightLaunchAttempts = 3;

    /// <summary>Default pause between Moonlight launch attempts (Req 9.2).</summary>
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly DeviceConnection device;
    private readonly StreamingService forward;
    private readonly ISunshinePrerequisites prerequisites;
    private readonly MoonlightClient moonlight;
    private readonly IGameStreamHost host;
    private readonly ILogger<ReverseStreamingService> logger;

    /// <summary>Pause between Moonlight launch retries; overridable for fast tests (Req 9.2).</summary>
    private readonly TimeSpan retryDelay;

    /// <summary>
    /// Req 9.1/9.3: true only while this service owns a Sunshine instance it started in
    /// <see cref="StartAsync"/>. Used to avoid leaving an orphan process when the Thor
    /// drops off the hotspot, and to never stop a Sunshine the user started themselves.
    /// </summary>
    private bool ownsHost;

    public ReverseStreamingService(
        DeviceConnection device,
        StreamingService forward,
        ISunshinePrerequisites prerequisites,
        MoonlightClient moonlight,
        IGameStreamHost host,
        ILogger<ReverseStreamingService> logger)
        : this(device, forward, prerequisites, moonlight, host, logger, retryDelay: null)
    {
    }

    /// <summary>
    /// Test/internal seam that lets the Moonlight launch retry delay be overridden (e.g.
    /// to <see cref="TimeSpan.Zero"/>) so retry behaviour can be exercised without real
    /// waits. Mirrors how <see cref="DeviceConnection"/> takes an optional
    /// <c>pollInterval</c>. Production code uses the public constructor, which defaults
    /// the delay to <see cref="DefaultRetryDelay"/>.
    /// </summary>
    internal ReverseStreamingService(
        DeviceConnection device,
        StreamingService forward,
        ISunshinePrerequisites prerequisites,
        MoonlightClient moonlight,
        IGameStreamHost host,
        ILogger<ReverseStreamingService> logger,
        TimeSpan? retryDelay)
    {
        this.device = device;
        this.forward = forward;
        this.prerequisites = prerequisites;
        this.moonlight = moonlight;
        this.host = host;
        this.logger = logger;
        this.retryDelay = retryDelay ?? DefaultRetryDelay;

        // Req 3.3: when the Sunshine process exits (clean stop or crash), reflect it in
        // the UI. Mirror how StreamingService forwards the mirror's Exited event, but
        // translate it into a reverse-stream status so the UI can react uniformly.
        this.host.Exited += OnHostExited;

        // Req 9.1: the Thor can drop off its own hotspot mid-stream. DeviceConnection
        // raises StateChanged(null) when the device is lost; reflect that as a
        // disconnected reverse stream and clean up any Sunshine we started.
        this.device.StateChanged += OnDeviceStateChanged;
    }

    /// <summary>True while the Sunshine host process is running (Req 3.3).</summary>
    public bool IsRunning => host.IsRunning;

    /// <summary>
    /// Raised whenever the reverse-stream status changes: <see cref="ReverseStreamState.Active"/>
    /// after a successful start, and <see cref="ReverseStreamState.Inactive"/> when the
    /// Sunshine process exits. The UI subscribes to drive its state (Req 3.3).
    /// </summary>
    public event Action<ReverseStreamStatus>? StateChanged;

    /// <summary>
    /// Re-evaluates every prerequisite and returns the full list, PC-side and Thor-side.
    /// Prerequisites are checked fresh on each call — nothing is cached (Req 2.5), so a
    /// user who installs a missing piece and retries sees the updated state. Callers
    /// decide what to show by filtering on <see cref="Prerequisite.Ok"/>: anything with
    /// <c>Ok == false</c> is "what's missing" and carries guidance (Req 1.4).
    ///
    /// Order of evaluation:
    /// <list type="number">
    ///   <item><description>Req 1.5: when no Thor is connected the whole action is unavailable, so this returns a single not-ok "Thor conectado" prerequisite and skips the device-dependent checks (the Moonlight check needs the connection anyway).</description></item>
    ///   <item><description>Req 2.1/2.3/5/6: the PC-side checks from <see cref="ISunshinePrerequisites"/>.</description></item>
    ///   <item><description>Req 2.2/2.4: Moonlight installed on the Thor, with Play Store guidance when missing — no install is ever attempted.</description></item>
    /// </list>
    /// </summary>
    public async Task<IReadOnlyList<Prerequisite>> CheckPrerequisitesAsync(CancellationToken ct)
    {
        // Req 1.5: no Thor connected → the reverse action is unavailable. Return a clear,
        // not-ok prerequisite rather than probing device-dependent checks that can't run.
        if (device.Current is null)
        {
            logger.LogInformation("Reverse streaming unavailable: no Thor connected");
            return
            [
                new Prerequisite(
                    "Thor conectado",
                    false,
                    "Conecte o Thor para usar o PC no Thor. A ação fica indisponível sem o Thor conectado."),
            ];
        }

        // Req 2.5: re-evaluate on every call; nothing is cached. The PC-side checks and
        // the Thor-side Moonlight check are independent, so run them concurrently.
        var pcChecks = prerequisites.CheckAsync(ct);
        var moonlightCheck = CheckMoonlightAsync(ct);
        await Task.WhenAll(pcChecks, moonlightCheck);

        var result = new List<Prerequisite>(await pcChecks) { await moonlightCheck };
        var missing = result.Count(p => !p.Ok);
        logger.LogInformation(
            "Reverse streaming prerequisites: {Total} checked, {Missing} missing",
            result.Count, missing);

        return result;
    }

    /// <summary>
    /// Entry point for the reverse direction (PC → Thor). The two directions must never
    /// run at once (Req 7), so the very first step is to end the forward direction: if
    /// scrcpy is mirroring the Thor, it is stopped before anything else happens.
    ///
    /// After that it re-checks prerequisites (fresh on every attempt, Req 2.5) and gates
    /// on them: if anything is missing, it returns a failure whose message lists what's
    /// missing so the UI can guide the user (Req 1.4) — nothing is installed.
    ///
    /// After the gate passes, it runs the happy path (Req 1.3): ensure Sunshine is up,
    /// then open Moonlight on the Thor, and move the state to active.
    /// </summary>
    /// <param name="pcIp">
    /// The PC's IP on the hotspot, used only to guide the user on which host to tap in
    /// Moonlight (Req 4.3/4.4) — the official app can't auto-connect via intent, so this
    /// is never passed to the app. The UI supplies it from the hotspot gateway flow; when
    /// unknown it may be omitted and the launch still proceeds.
    /// </param>
    public async Task<Result> StartAsync(string? pcIp = null, CancellationToken ct = default)
    {
        // Req 7: enforce mutual exclusion first — starting the reverse ends scrcpy.
        StopForwardIfRunning();

        // Req 2.5 / 1.4: re-evaluate prerequisites and gate on them. Only the BLOCKING
        // prerequisites (Sunshine/Moonlight installed, Thor connected) can stop the
        // start; the best-effort checks (hardware encoder, Wayland capture, Sunshine
        // already running) are informational and must NEVER block (Req 5/6, design
        // "informacional e nunca bloqueia"). A not-ok encoder means Sunshine may fall
        // back to software encode — the UI warns about the performance impact (Req 5.2),
        // but the stream is still allowed to start.
        var prereqs = await CheckPrerequisitesAsync(ct);
        var blockingMissing = prereqs.Where(p => !p.Ok && p.IsBlocking).ToList();
        if (blockingMissing.Count > 0)
        {
            var names = string.Join(", ", blockingMissing.Select(p => p.Name));
            logger.LogInformation("Reverse streaming blocked: missing {Missing}", names);
            return Result.Failure(new Error(
                "REVERSE_PREREQUISITES_MISSING",
                $"Não é possível usar o PC no Thor. Faltando: {names}."));
        }

        // Req 1.3 happy path, in order: Sunshine first, then Moonlight, then state.

        // Req 3.1/3.2: ensure Sunshine is running. Only start it when it isn't already up
        // (never a second instance). v1 uses Sunshine's default profile/desktop, so no
        // args are needed — Sunshine reads its own config.
        var startedHere = false;
        if (!host.IsRunning)
        {
            try
            {
                host.Start("");
                startedHere = true;
                logger.LogInformation("Sunshine started for reverse streaming");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start Sunshine");
                return Result.Failure(new Error(
                    "SUNSHINE_START_FAILED",
                    $"Não foi possível iniciar o Sunshine: {ex.Message}"));
            }
        }
        else
        {
            logger.LogInformation("Sunshine already running; not starting a second instance");
        }

        // Mark ownership now that Sunshine is up: if the Thor drops before the stream is
        // active, OnDeviceStateChanged uses this to tear the host down (Req 9.1/9.3).
        if (startedHere)
            ownsHost = true;

        // Req 4.2/4.3 + Req 9.2: open Moonlight on the Thor. The ADB launch can be flaky
        // (especially right after the hotspot settles), so retry a few times with a short
        // delay before giving up. The IP is guidance-only (the official app can't
        // auto-connect via intent). If every attempt fails, don't leave a Sunshine
        // instance we just started running — stop it and surface the failure (Req 9.3)
        // with a message that makes clear the user can retry (Req 9.2).
        var launch = await LaunchMoonlightWithRetryAsync(pcIp ?? string.Empty, ct);
        if (!launch.IsSuccess)
        {
            if (startedHere)
            {
                logger.LogInformation("Stopping Sunshine after a failed Moonlight launch");
                host.Stop();
                ownsHost = false;
            }
            return launch;
        }

        // Req 3.3: reflect the running reverse stream so the UI can react.
        logger.LogInformation("Reverse streaming active");
        StateChanged?.Invoke(new ReverseStreamStatus(ReverseStreamState.Active));
        return Result.Success();
    }

    /// <summary>
    /// Stops the Sunshine host this service started, so no reverse-streaming process is
    /// left running. Used by the UI coordinator to end the reverse direction (Req 7) and
    /// to avoid orphan processes (Req 9.3). Clearing ownership leaves the service in a
    /// re-startable state so a later <see cref="StartAsync"/> can run cleanly (Req 9.2).
    /// </summary>
    public void Stop()
    {
        ownsHost = false;
        host.Stop();
    }

    /// <summary>
    /// Req 9.2: opens Moonlight on the Thor, retrying a few transient failures with a
    /// short pause in between. The pause length comes from <see cref="retryDelay"/> so
    /// tests can run retries with zero delay. Honours cancellation both during the launch
    /// and during the wait. Returns the first success, or the last failure (reusing
    /// MoonlightClient's <c>MOONLIGHT_LAUNCH_FAILED</c>) once attempts are exhausted.
    /// </summary>
    private async Task<Result> LaunchMoonlightWithRetryAsync(string pcIp, CancellationToken ct)
    {
        Result launch = Result.Success();

        for (var attempt = 1; attempt <= MoonlightLaunchAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            launch = await moonlight.LaunchAsync(pcIp, ct);
            if (launch.IsSuccess)
            {
                if (attempt > 1)
                    logger.LogInformation("Moonlight opened on attempt {Attempt}", attempt);
                return launch;
            }

            logger.LogWarning(
                "Moonlight launch attempt {Attempt}/{Total} failed: {Error}",
                attempt, MoonlightLaunchAttempts, launch.Error!.Code);

            if (attempt < MoonlightLaunchAttempts && retryDelay > TimeSpan.Zero)
                await Task.Delay(retryDelay, ct);
        }

        logger.LogWarning("Giving up on Moonlight after {Total} attempts", MoonlightLaunchAttempts);
        return launch;
    }

    /// <summary>
    /// Req 3.3: the Sunshine process ended (clean stop or crash). Reflect it as an
    /// inactive reverse stream so the UI drops out of the active state. v1 can't
    /// distinguish a crash from a normal stop here, so it reports inactive with a
    /// generic reason; a specific error path is a later concern. The host is gone, so we
    /// no longer own one — clear ownership to stay re-startable (Req 9.2).
    /// </summary>
    private void OnHostExited()
    {
        ownsHost = false;
        StateChanged?.Invoke(new ReverseStreamStatus(
            ReverseStreamState.Inactive,
            "O Sunshine foi encerrado."));
    }

    /// <summary>
    /// Req 9.1: the shared <see cref="DeviceConnection"/> changed state. When it reports
    /// the Thor was lost (<c>null</c>) and a reverse stream we started is still running,
    /// the stream can't survive — reflect a disconnected error state and tear down the
    /// Sunshine instance we own so it isn't left orphaned (Req 9.3). We only act while
    /// something is actually running: a <c>null</c> fired when nothing is active is just
    /// the idle UI, not a dropped stream, so we don't emit spurious transitions.
    /// </summary>
    private void OnDeviceStateChanged(ThorDevice? current)
    {
        // A (re)connection or a drop while idle needs no reverse-stream reaction.
        if (current is not null || !host.IsRunning)
            return;

        logger.LogWarning("Thor dropped off the hotspot during reverse streaming");

        if (ownsHost)
        {
            logger.LogInformation("Stopping the Sunshine we started to avoid an orphan process");
            ownsHost = false;
            host.Stop();
        }

        StateChanged?.Invoke(new ReverseStreamStatus(
            ReverseStreamState.Error,
            "O Thor caiu da rede."));
    }

    /// <summary>
    /// Req 7: "encerrar o scrcpy antes de iniciar o reverso." If the forward direction
    /// (Thor→PC) is mirroring, stop it so the two directions never run at once.
    /// </summary>
    private void StopForwardIfRunning()
    {
        if (!forward.IsRunning)
            return;

        logger.LogInformation("Stopping forward streaming (scrcpy) before starting reverse");
        forward.Stop();
    }

    /// <summary>
    /// Req 2.2/2.4: checks Moonlight on the Thor via ADB and maps it to a
    /// <see cref="Prerequisite"/>. When it is not installed, the guidance points to the
    /// Play Store and nothing is installed automatically.
    /// </summary>
    private async Task<Prerequisite> CheckMoonlightAsync(CancellationToken ct)
    {
        var installed = await moonlight.IsInstalledAsync(ct);

        return installed
            ? new Prerequisite("Moonlight instalado no Thor", true)
            : new Prerequisite(
                "Moonlight instalado no Thor",
                false,
                "O Moonlight não foi encontrado no Thor. Instale-o pela Play Store e tente novamente.",
                MoonlightStoreUrl);
    }
}
