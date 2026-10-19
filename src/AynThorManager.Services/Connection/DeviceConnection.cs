using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Core.Validators;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Connection;

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

    public ThorDevice? Current { get { lock (_lock) return _current; } }

    public bool IsConnected => Current is not null;

    public event Action<ThorDevice?>? StateChanged;

    private async Task<IReadOnlyList<AdbDeviceEntry>> ListDevicesAsync(CancellationToken ct)
    {
        var result = await RawAsync("devices -l", QuickTimeout, ct);
        return result.IsSuccess ? AdbOutputParser.ParseDevices(result.Value!.StandardOutput) : [];
    }

    /// <summary>
    /// One-time setup over USB: with the Thor plugged in (USB debugging authorized),
    /// switches its adb daemon to listen on a fixed TCP port (5555). After this the
    /// cable can be removed and the device is reachable over Wi-Fi at that port —
    /// including when the Thor is acting as a hotspot (unlike mDNS wireless debugging).
    /// </summary>
    public async Task<Result<ThorDevice>> PrepareUsbTcpipAsync(CancellationToken ct)
    {
        var usb = (await ListDevicesAsync(ct))
            .FirstOrDefault(d => d.Transport == DeviceTransport.Usb);

        if (usb is null)
            return Fail("USB_NOT_FOUND",
                "Nenhum Thor encontrado via USB. Conecte o cabo e ative a Depuração USB no Thor.");

        if (usb is { State: "unauthorized" })
            return Fail("UNAUTHORIZED",
                "Aceite o pedido de Depuração USB na tela do Thor e tente de novo.");

        if (!usb.IsReady)
            return Fail("USB_NOT_READY", $"O Thor via USB não está pronto (estado: {usb.State}).");

        var tcpip = await RawAsync($"-s {usb.Serial} tcpip {DefaultAdbPort}", QuickTimeout, ct);
        if (!tcpip.IsSuccess || tcpip.Value!.ExitCode != 0)
        {
            var detail = tcpip.Value is null ? tcpip.Error?.Message : tcpip.Value.StandardError.Trim();
            return Fail("TCPIP_FAILED", $"Não foi possível preparar o modo sem fio: {detail}");
        }

        logger.LogInformation("Thor {Serial} switched to tcpip:{Port}", usb.Serial, DefaultAdbPort);
        return Result<ThorDevice>.Success(new ThorDevice(usb.Serial, DeviceTransport.Usb, usb.Model?.Replace('_', ' ') ?? "Android"));
    }

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

        AdbDeviceEntry? entry = null;
        for (var attempt = 0; attempt < ReadyPollAttempts; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(_pollInterval, ct);

            entry = PickForSerial(await ListDevicesAsync(ct), target);
            if (entry is { IsReady: true } or { State: "unauthorized" })
                break;
        }

        if (entry is { IsReady: true })
            return Adopt(entry);

        if (entry is { State: "unauthorized" })
            return Fail("UNAUTHORIZED", "O Thor recusou a conexão. Aceite o pedido de depuração na tela do Thor e tente de novo.");

        return Fail("CONNECTION_FAILED", $"Não foi possível conectar em {target}. Confira o IP e a porta em 'Depuração sem fio' no Thor.");
    }

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

    private static bool IsExecutorTimeout(CommandResult result) =>
        result.ExitCode == -1 && result.StandardError.StartsWith("Command timed out", StringComparison.Ordinal);

    private Task<Result<CommandResult>> RawAsync(string arguments, TimeSpan timeout, CancellationToken ct) =>
        queue.EnqueueAsync(new AdbCommand(arguments, timeout, arguments), ct);

    /// <summary>
    /// Picks a single, stable entry for a serial when <c>adb devices</c> lists the same
    /// Thor more than once (e.g. a stale <c>offline</c> alongside the live <c>device</c>).
    /// Prefers the ready (<c>device</c>) entry, then an <c>unauthorized</c> one, and only
    /// then anything else — so a leftover offline duplicate never wins the selection and
    /// we never command the wrong target (Req 5.2). Ties keep adb's listing order.
    /// </summary>
    private static AdbDeviceEntry? PickForSerial(IReadOnlyList<AdbDeviceEntry> devices, string serial) =>
        devices
            .Where(d => d.Serial == serial)
            .OrderBy(StatePriority)
            .FirstOrDefault();

    private static int StatePriority(AdbDeviceEntry entry) => entry switch
    {
        { IsReady: true } => 0,
        { State: "unauthorized" } => 1,
        _ => 2,
    };

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
