using CliWrap;
using CliWrap.Buffered;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Infrastructure.Network;

public sealed class WifiConnectionService : IWifiConnection
{
    private const string ProfileName = "AynThorDirect";

    public async Task<(bool Ok, string? ThorIp, string? Error)> ConnectAsync(
        string ssid, string password, CancellationToken ct)
    {
        var iface = await DetectWifiInterfaceAsync(ct);
        if (iface is null)
            return (false, null, "Nenhuma interface Wi-Fi encontrada no PC.");

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

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await RunAsync("nmcli", new[] { "connection", "down", ProfileName }, ct);
        await RunAsync("nmcli", new[] { "connection", "delete", ProfileName }, ct);
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

    private static async Task<string?> GetGatewayAsync(string iface, CancellationToken ct)
    {
        var result = await RunAsync("ip", new[] { "-4", "route", "show", "default", "dev", iface }, ct);
        if (result.ExitCode != 0)
            return null;

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
