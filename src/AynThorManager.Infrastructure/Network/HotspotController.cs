using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using CliWrap;
using CliWrap.Buffered;

namespace AynThorManager.Infrastructure.Network;

public sealed class HotspotController : IHotspotController
{
    private readonly string _wifiInterface;
    private readonly string _connectionName;

    public HotspotController(string wifiInterface, string connectionName = "AynThorDirect")
    {
        _wifiInterface = wifiInterface;
        _connectionName = connectionName;
    }

    public static async Task<string?> DetectWifiInterfaceAsync(CancellationToken ct = default)
    {
        var result = await RunAsync("nmcli", new[] { "-t", "-f", "DEVICE,TYPE", "device" }, ct);
        if (result.ExitCode != 0)
            return null;

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':');
            if (parts.Length >= 2 && parts[1].Trim() == "wifi")
                return parts[0].Trim();
        }

        return null;
    }

    public async Task<HotspotResult> StartAsync(string ssid, string password, CancellationToken ct)
    {
        if (password.Length < 8)
            return HotspotResult.Fail("A senha do hotspot precisa de ao menos 8 caracteres.");

        var result = await RunAsync("nmcli",
            new[] { "device", "wifi", "hotspot",
                    "ifname", _wifiInterface,
                    "con-name", _connectionName,
                    "ssid", ssid,
                    "password", password },
            ct);

        if (result.ExitCode != 0)
        {
            var detail = (result.StandardError + " " + result.StandardOutput).Trim();
            return HotspotResult.Fail($"Não foi possível criar o hotspot: {detail}");
        }

        return HotspotResult.Ok();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await RunAsync("nmcli", new[] { "connection", "down", _connectionName }, ct);
        await RunAsync("nmcli", new[] { "connection", "delete", _connectionName }, ct);
    }

    public async Task<IReadOnlyList<NetworkClient>> ListClientsAsync(CancellationToken ct)
    {
        var result = await RunAsync("ip", new[] { "neigh", "show", "dev", _wifiInterface }, ct);
        return result.ExitCode == 0 ? IpNeighborParser.Parse(result.StandardOutput) : [];
    }

    private static async Task<BufferedCommandResult> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            return await Cli.Wrap(file)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);
        }
        catch (Exception ex)
        {
            return new BufferedCommandResult(-1, DateTimeOffset.Now, DateTimeOffset.Now, string.Empty, ex.Message);
        }
    }
}
