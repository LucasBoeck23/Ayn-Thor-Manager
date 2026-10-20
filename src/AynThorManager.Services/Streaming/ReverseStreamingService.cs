using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Streaming;

public sealed class ReverseStreamingService
{
    private const string MoonlightStoreUrl =
        "https://play.google.com/store/apps/details?id=com.limelight";

    private const int MoonlightLaunchAttempts = 3;

    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly DeviceConnection device;
    private readonly StreamingService forward;
    private readonly ISunshinePrerequisites prerequisites;
    private readonly MoonlightClient moonlight;
    private readonly IGameStreamHost host;
    private readonly ILogger<ReverseStreamingService> logger;

    private readonly TimeSpan retryDelay;

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

        this.host.Exited += OnHostExited;

        this.device.StateChanged += OnDeviceStateChanged;
    }

    public bool IsRunning => host.IsRunning;

    public event Action<ReverseStreamStatus>? StateChanged;

    public async Task<IReadOnlyList<Prerequisite>> CheckPrerequisitesAsync(CancellationToken ct)
    {
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

    public async Task<Result> StartAsync(string? pcIp = null, CancellationToken ct = default)
    {
        StopForwardIfRunning();

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

        if (startedHere)
            ownsHost = true;

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

        logger.LogInformation("Reverse streaming active");
        StateChanged?.Invoke(new ReverseStreamStatus(ReverseStreamState.Active));
        return Result.Success();
    }

    public void Stop()
    {
        ownsHost = false;
        host.Stop();
    }

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

    private void OnHostExited()
    {
        ownsHost = false;
        StateChanged?.Invoke(new ReverseStreamStatus(
            ReverseStreamState.Inactive,
            "O Sunshine foi encerrado."));
    }

    private void OnDeviceStateChanged(ThorDevice? current)
    {
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

    private void StopForwardIfRunning()
    {
        if (!forward.IsRunning)
            return;

        logger.LogInformation("Stopping forward streaming (scrcpy) before starting reverse");
        forward.Stop();
    }

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
