using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Network;

/// <summary>
/// Joins the PC to the Thor's own Wi-Fi hotspot (the Thor is the access point) and
/// reports the Thor's IP, which is the gateway of that network.
///
/// This replaces the fragile "PC becomes the hotspot" approach: being a Wi-Fi client
/// is a rock-solid, root-free operation, and modern Android handhelds create a clean
/// 5 GHz AP, so this gives a stable low-latency link without touching hostapd/drivers.
/// </summary>
public sealed class WifiConnectionService : IWifiConnection
{
    private const string ProfileName = "AynThorDirect";

    /// <summary>
    /// Connects to the hotspot <paramref name="ssid"/> and returns the Thor's IP
    /// (the default gateway on that interface), or an error message on failure.
    /// </summary>
    public async Task<(bool Ok, string? ThorIp, string? Error)> ConnectAsync(
        string ssid, string password, CancellationToken ct)
    {
        var iface = await DetectWifiInterfaceAsync(ct);
        if (iface is null)
            return (false, null, "Nenhuma interface Wi-Fi encontrada no PC.");

        // Rebuild the profile fresh each time so a stale/mismatched one (e.g. a WPA3
        // entry from a previous attempt) can't block the connection. WPA2-PSK is set
        // explicitly because `nmcli dev wifi connect` guesses the key-mgmt and fails
        // on some adapters.
        await RunAsync("nmcli", new[] { "connection", "delete", ProfileName }, ct);

        await RunAsync("nmcli", new[] { "device", "wifi", "rescan" }, ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        var add = await RunAsync("nmcli",
            new[] { "connection", "add", "type", "wifi",
                    "con-name", ProfileName, "ifname", iface, "ssid", ssid },
            ct);
        if (add.ExitCode != 0)
            return (false, null, $"Falha ao preparar a conexão: {Detail(add)}");

        var modify = await RunAsync("nmcli",
            new[] { "connection", "modify", ProfileName,
                    "802-11-wireless-security.key-mgmt", "wpa-psk",
                    "802-11-wireless-security.psk", password },
            ct);
        if (modify.ExitCode != 0)
            return (false, null, $"Falha ao configurar a senha: {Detail(modify)}");

        var up = await RunAsync("nmcli", new[] { "connection", "up", ProfileName }, ct);
        if (up.ExitCode != 0)
            return (false, null, $"Não foi possível conectar à rede \"{ssid}\": {Detail(up)}");

        var ip = await GetGatewayAsync(iface, ct);
        if (ip is null)
            return (false, null, "Conectado, mas não encontrei o IP do Thor na rede.");

        return (true, ip, null);
    }

    /// <summary>Disconnects from the hotspot and removes the profile.</summary>
    public async Task DisconnectAsync(CancellationToken ct)
    {
        await RunAsync("nmcli", new[] { "connection", "down", ProfileName }, ct);
        await RunAsync("nmcli", new[] { "connection", "delete", ProfileName }, ct);
    }

    /// <summary>Finds the first Wi-Fi interface name (e.g. "wlp2s0") via nmcli.</summary>
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

    /// <summary>Reads the default gateway on the interface — that is the Thor itself.</summary>
    private static async Task<string?> GetGatewayAsync(string iface, CancellationToken ct)
    {
        var result = await RunAsync("ip", new[] { "-4", "route", "show", "default", "dev", iface }, ct);
        if (result.ExitCode != 0)
            return null;

        // e.g. "default via 192.168.154.104 proto dhcp src 192.168.154.37 metric 600"
        var tokens = result.StandardOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var viaIndex = Array.IndexOf(tokens, "via");
        return viaIndex >= 0 && viaIndex + 1 < tokens.Length ? tokens[viaIndex + 1] : null;
    }

    private static string Detail(BufferedCommandResult r) =>
        (r.StandardError + " " + r.StandardOutput).Trim();

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
