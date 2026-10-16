using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using CliWrap;
using CliWrap.Buffered;

namespace AynThorManager.Infrastructure.Network;

/// <summary>
/// Creates a direct Wi-Fi link between the PC and the Thor by turning the PC's
/// Wi-Fi adapter into an access point (hotspot) via NetworkManager (<c>nmcli</c>),
/// then discovers connected clients via the kernel neighbor table (<c>ip neigh</c>).
///
/// This gives a dedicated two-device network (no router, no interference), which
/// is far more stable and lower-latency than a shared home Wi-Fi.
///
/// Isolated from the rest of the app: it only shells out to nmcli/ip and parses
/// their output. It does not touch adb or the existing connection flow.
/// </summary>
public sealed class HotspotController : IHotspotController
{
    private readonly string _wifiInterface;
    private readonly string _connectionName;

    public HotspotController(string wifiInterface, string connectionName = "AynThorDirect")
    {
        _wifiInterface = wifiInterface;
        _connectionName = connectionName;
    }

    /// <summary>
    /// Finds the first Wi-Fi interface name (e.g. "wlp2s0") via nmcli, or null if
    /// none is found or nmcli is unavailable. Used to pick the adapter to host the
    /// hotspot on.
    /// </summary>
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

    /// <summary>
    /// Brings up the hotspot. On success the PC serves a Wi-Fi network with the
    /// given SSID/password that the Thor can join. Returns the error text on failure.
    /// </summary>
    public async Task<HotspotResult> StartAsync(string ssid, string password, CancellationToken ct)
    {
        if (password.Length < 8)
            return HotspotResult.Fail("A senha do hotspot precisa de ao menos 8 caracteres.");

        // Let the driver pick the band. Forcing 5 GHz ("band a") hangs on this
        // adapter (rtw88/RTL8821CE), so we don't; the default band is reliable.
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

    /// <summary>Tears the hotspot down and lets the adapter return to normal use.</summary>
    public async Task StopAsync(CancellationToken ct)
    {
        // Deleting the connection profile both stops it and removes it.
        await RunAsync("nmcli", new[] { "connection", "down", _connectionName }, ct);
        await RunAsync("nmcli", new[] { "connection", "delete", _connectionName }, ct);
    }

    /// <summary>
    /// Lists the devices currently seen on the hotspot interface. The Thor shows
    /// up here once it joins and gets a DHCP lease.
    /// </summary>
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
            // Surface a failed exit code with the message instead of throwing, so the
            // caller handles "nmcli/ip not found" the same way as a command error.
            return new BufferedCommandResult(-1, DateTimeOffset.Now, DateTimeOffset.Now, string.Empty, ex.Message);
        }
    }
}
