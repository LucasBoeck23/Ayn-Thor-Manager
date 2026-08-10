using AynThorManager.Api.Middleware;
using AynThorManager.Core.DTOs;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for device connection management (/api/device).
/// </summary>
public static class DeviceEndpoints
{
    public static RouteGroupBuilder MapDeviceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/device")
            .WithTags("Device");

        group.MapPost("/connect", ConnectAsync)
            .WithName("ConnectDevice")
            .Produces<DeviceStatusDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapPost("/disconnect", DisconnectAsync)
            .WithName("DisconnectDevice")
            .Produces<DeviceStatusDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/status", GetStatus)
            .WithName("GetDeviceStatus")
            .Produces<DeviceStatusDto>(StatusCodes.Status200OK);

        group.MapGet("/scan", ScanNetworkAsync)
            .WithName("ScanDevices")
            .WithSummary("Scan local network for ADB devices");

        group.MapPost("/pair", PairAsync)
            .WithName("PairDevice")
            .WithSummary("Pair with a device using wireless debugging code");

        return group;
    }

    private static async Task<IResult> ConnectAsync(
        ConnectRequestDto request,
        IAdbConnectionManager connectionManager,
        CancellationToken ct)
    {
        var result = await connectionManager.ConnectAsync(request.IpAddress, ct);

        if (result.IsSuccess)
        {
            return TypedResults.Ok(ToDto(result.Value!));
        }

        return ErrorMapping.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> DisconnectAsync(
        IAdbConnectionManager connectionManager,
        CancellationToken ct)
    {
        var result = await connectionManager.DisconnectAsync(ct);

        if (result.IsSuccess)
        {
            return TypedResults.Ok(ToDto(result.Value!));
        }

        return ErrorMapping.ToProblemResult(result.Error!);
    }

    private static IResult GetStatus(IAdbConnectionManager connectionManager)
    {
        var status = connectionManager.CurrentStatus;
        return TypedResults.Ok(ToDto(status));
    }

    private static DeviceStatusDto ToDto(DeviceStatus status) => new(
        Status: status.Status switch
        {
            DeviceStatusType.Connected => "conectado",
            DeviceStatusType.Disconnected => "desconectado",
            DeviceStatusType.Unauthorized => "não autorizado",
            _ => "desconhecido"
        },
        IpAddress: status.IpAddress,
        Message: status.Message);

    private static async Task<IResult> ScanNetworkAsync(
        IAdbCommandExecutor executor,
        IAdbConnectionManager connectionManager,
        CancellationToken ct)
    {
        var found = new List<FoundDevice>();

        // 1. Quick check: already connected?
        try
        {
            var devices = await executor.ExecuteAsync("devices", TimeSpan.FromSeconds(3), ct);
            if (devices.Success)
            {
                foreach (var line in devices.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("List") || !line.Contains('\t')) continue;
                    var parts = line.Split('\t');
                    if (parts.Length >= 2 && parts[0].Contains(':'))
                    {
                        var addr = parts[0].Trim();
                        var deviceName = await GetDeviceNameAsync(executor, addr, ct);
                        found.Add(new FoundDevice(addr, deviceName ?? "Android Device", "connected", "device"));
                    }
                }
            }
        }
        catch { }

        // If already connected, return immediately
        if (found.Count > 0)
        {
            // Sync internal state
            if (!connectionManager.IsConnected)
                await connectionManager.ConnectAsync(found[0].Address, ct);
            return TypedResults.Ok(new { devices = found, localIp = GetLocalIp(), autoConnected = true });
        }

        // 2. mDNS discovery
        try
        {
            var mdns = await executor.ExecuteAsync("mdns services", TimeSpan.FromSeconds(3), ct);
            if (mdns.Success && !string.IsNullOrWhiteSpace(mdns.StandardOutput))
            {
                foreach (var line in mdns.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("List")) continue;
                    var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        var endpoint = parts[^1].Trim();
                        if (endpoint.Contains(':') && !endpoint.StartsWith("*"))
                        {
                            // Try to connect
                            var connectResult = await executor.ExecuteAsync($"connect {endpoint}", TimeSpan.FromSeconds(5), ct);
                            if (connectResult.Success && connectResult.StandardOutput.ToLowerInvariant().Contains("connected"))
                            {
                                var deviceName = await GetDeviceNameAsync(executor, endpoint, ct);
                                found.Add(new FoundDevice(endpoint, deviceName ?? parts[0].Trim(), "mdns", "connected"));
                            }
                        }
                    }
                }
            }
        }
        catch { }

        if (found.Count > 0)
        {
            await connectionManager.ConnectAsync(found[0].Address, ct);
            return TypedResults.Ok(new { devices = found, localIp = GetLocalIp(), autoConnected = true });
        }

        // 3. Brute force: get all IPs from ARP table, try ADB connect on common ports
        var localIp = GetLocalIp();
        var portsToTry = new[] { 5555, 38383, 37000, 39000, 40000, 41000, 42000, 43000, 44000, 45000 };

        try
        {
            var arpProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (arpProcess is not null)
            {
                var arpOutput = await arpProcess.StandardOutput.ReadToEndAsync(ct);
                await arpProcess.WaitForExitAsync(ct);

                var arpIps = System.Text.RegularExpressions.Regex.Matches(arpOutput, @"(\d+\.\d+\.\d+\.\d+)")
                    .Select(m => m.Groups[1].Value)
                    .Where(ip => ip != localIp && !ip.EndsWith(".255") && !ip.EndsWith(".1"))
                    .Distinct()
                    .ToList();

                // Try connecting to each IP on each port — TCP check first (fast), then adb connect
                foreach (var ip in arpIps)
                {
                    if (ct.IsCancellationRequested || found.Count > 0) break;

                    foreach (var port in portsToTry)
                    {
                        if (ct.IsCancellationRequested || found.Count > 0) break;

                        // Quick TCP check (150ms) — skip if port is closed
                        if (!await IsPortOpenAsync(ip, port))
                            continue;

                        // Port is open! Try adb connect
                        try
                        {
                            var target = $"{ip}:{port}";
                            var connectResult = await executor.ExecuteAsync($"connect {target}", TimeSpan.FromSeconds(3), ct);
                            var output = connectResult.StandardOutput.ToLowerInvariant();

                            if (output.Contains("connected"))
                            {
                                var deviceName = await GetDeviceNameAsync(executor, target, ct);
                                found.Add(new FoundDevice(target, deviceName ?? "Android Device", "scan", "connected"));
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        if (found.Count > 0)
            await connectionManager.ConnectAsync(found[0].Address, ct);

        return TypedResults.Ok(new { devices = found, localIp, autoConnected = found.Count > 0 });
    }

    private static async Task<string?> GetDeviceNameAsync(IAdbCommandExecutor executor, string serial, CancellationToken ct)
    {
        try
        {
            var result = await executor.ExecuteAsync($"-s {serial} shell getprop ro.product.model", TimeSpan.FromSeconds(2), ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.StandardOutput))
                return result.StandardOutput.Trim();
        }
        catch { }
        return null;
    }

    private sealed record FoundDevice(string Address, string Name, string Source, string Status);

    private static async Task<IResult> PairAsync(
        PairRequestDto request,
        IAdbCommandExecutor executor,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Code))
            return TypedResults.BadRequest(new { error = "Host e codigo sao obrigatorios." });

        try
        {
            // adb pair host:port code
            var result = await executor.ExecuteAsync($"pair {request.Host} {request.Code}", TimeSpan.FromSeconds(10), ct);
            var output = $"{result.StandardOutput} {result.StandardError}".ToLowerInvariant();

            if (output.Contains("successfully paired"))
                return TypedResults.Ok(new { success = true, message = "Pareamento realizado com sucesso!" });

            if (output.Contains("failed"))
                return TypedResults.Ok(new { success = false, message = "Falha no pareamento. Verifique o codigo e tente novamente." });

            return TypedResults.Ok(new { success = result.Success, message = result.StandardOutput + result.StandardError });
        }
        catch (Exception ex)
        {
            return TypedResults.Ok(new { success = false, message = $"Erro: {ex.Message}" });
        }
    }

    private sealed record PairRequestDto(string Host, string Code);

    private static string? GetLocalIp()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as System.Net.IPEndPoint)?.Address.ToString();
        }
        catch { return null; }
    }

    private static async Task<bool> IsPortOpenAsync(string ip, int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            using var cts = new CancellationTokenSource(150);
            await client.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch { return false; }
    }
}
