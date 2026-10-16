using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Core.Validators;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Connection;

/// <summary>
/// Single entry point for everything related to the device connection.
/// It owns which device is in use and is the only component that builds adb
/// commands targeting it, so every command carries the right <c>-s serial</c>.
/// </summary>
public sealed class DeviceConnection(
    ICommandQueue queue,
    ILogger<DeviceConnection> logger,
    TimeSpan? pollInterval = null)
{
    private const int DefaultAdbPort = 5555;
    private const int CompanionPort = 7100;
    private const int ReadyPollAttempts = 6;
    private static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);

    private readonly object _lock = new();
    private ThorDevice? _current;

    /// <summary>Device in use, or null when disconnected.</summary>
    public ThorDevice? Current { get { lock (_lock) return _current; } }

    public bool IsConnected => Current is not null;

    /// <summary>Raised on connect, device switch and connection loss (argument is null).</summary>
    public event Action<ThorDevice?>? StateChanged;

    /// <summary>Devices adb currently knows about, with their state (used to confirm a connect).</summary>
    private async Task<IReadOnlyList<AdbDeviceEntry>> ListDevicesAsync(CancellationToken ct)
    {
        var result = await RawAsync("devices -l", QuickTimeout, ct);
        return result.IsSuccess ? AdbOutputParser.ParseDevices(result.Value!.StandardOutput) : [];
    }

    /// <summary>Wi-Fi connection to <c>ip:port</c> (the hotspot-discovered endpoint).</summary>
    public async Task<Result<ThorDevice>> ConnectAsync(string address, CancellationToken ct)
    {
        var target = (address ?? "").Trim();
        if (target.Length == 0)
            return Fail("INVALID_ADDRESS", "Digite o IP do Thor.");

        if (!target.Contains(':'))
            target += $":{DefaultAdbPort}";

        var validation = IpAddressValidator.Validate(target);
        if (!validation.IsSuccess)
            return Fail(validation.Error!);

        if (target.EndsWith($":{CompanionPort}"))
            return Fail("COMPANION_PORT",
                "A porta 7100 é do Ayn Thor Link, não do ADB. Use a porta mostrada em 'Depuração sem fio' no Thor.");

        var connect = await RawAsync($"connect {target}", ConnectTimeout, ct);
        if (!connect.IsSuccess || connect.Value is null)
            return Fail("CONNECTION_FAILED", $"Não foi possível conectar em {target}.");

        var connectResult = connect.Value;
        if (IsExecutorTimeout(connectResult))
            return Fail("TIMEOUT", $"O Thor não respondeu em {target}. Confira se ele está na mesma rede e com a tela ligada.");

        var output = $"{connectResult.StandardOutput} {connectResult.StandardError}".Trim();
        if (output.Contains("cannot connect", StringComparison.OrdinalIgnoreCase)
            || output.Contains("failed to connect", StringComparison.OrdinalIgnoreCase))
            return Fail("CONNECTION_FAILED", $"Não foi possível conectar em {target}: {output}");

        // The text and exit code of `adb connect` are unreliable (it prints "already
        // connected" for dead endpoints), so only trust what `adb devices` reports.
        AdbDeviceEntry? entry = null;
        for (var attempt = 0; attempt < ReadyPollAttempts; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(_pollInterval, ct);

            entry = (await ListDevicesAsync(ct)).FirstOrDefault(d => d.Serial == target);
            if (entry is { IsReady: true } or { State: "unauthorized" })
                break;
        }

        if (entry is { IsReady: true })
            return Adopt(entry);

        if (entry is { State: "unauthorized" })
            return Fail("UNAUTHORIZED", "O Thor recusou a conexão. Aceite o pedido de depuração na tela do Thor e tente de novo.");

        return Fail("CONNECTION_FAILED", $"Não foi possível conectar em {target}. Confira o IP e a porta em 'Depuração sem fio' no Thor.");
    }

    /// <summary>
    /// Runs an adb command against the current device. If adb reports the device is
    /// gone, the connection state is cleared so the UI reflects it.
    /// </summary>
    public async Task<Result<CommandResult>> RunAsync(string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var device = Current;
        if (device is null)
            return Result<CommandResult>.Failure(new Error("DEVICE_NOT_CONNECTED", "O Thor não está conectado."));

        var result = await RawAsync($"-s {device.Serial} {arguments}", timeout, ct);

        if (result.IsSuccess && AdbOutputParser.IsDeviceLost(result.Value!.StandardError))
        {
            logger.LogWarning("Device {Serial} lost: {Error}", device.Serial, result.Value.StandardError.Trim());
            ClearIfCurrent(device);
            return Result<CommandResult>.Failure(new Error("DEVICE_LOST", "A conexão com o Thor caiu."));
        }

        return result;
    }

    /// <summary>AdbCommandExecutor reports its own timeout as exit code -1 with this message.</summary>
    private static bool IsExecutorTimeout(CommandResult result) =>
        result.ExitCode == -1 && result.StandardError.StartsWith("Command timed out", StringComparison.Ordinal);

    private Task<Result<CommandResult>> RawAsync(string arguments, TimeSpan timeout, CancellationToken ct) =>
        queue.EnqueueAsync(new AdbCommand(arguments, timeout, arguments), CommandPriority.Normal, ct);

    private Result<ThorDevice> Adopt(AdbDeviceEntry entry)
    {
        var device = new ThorDevice(entry.Serial, entry.Transport, entry.Model?.Replace('_', ' ') ?? "Android");
        logger.LogInformation("Using device {Serial} ({Transport}, {Model})", device.Serial, device.Transport, device.Model);
        SetCurrent(device);
        return Result<ThorDevice>.Success(device);
    }

    private void SetCurrent(ThorDevice? device)
    {
        lock (_lock)
        {
            if (_current == device)
                return;
            _current = device;
        }

        StateChanged?.Invoke(device);
    }

    private void ClearIfCurrent(ThorDevice device)
    {
        lock (_lock)
        {
            if (_current != device)
                return;
            _current = null;
        }

        StateChanged?.Invoke(null);
    }

    private static Result<ThorDevice> Fail(Error error) => Result<ThorDevice>.Failure(error);
    private static Result<ThorDevice> Fail(string code, string message) => Fail(new Error(code, message));
}
