using System.Text.RegularExpressions;

namespace AynThorManager.Services.Connection;

/// <summary>One line of <c>adb devices -l</c>.</summary>
public sealed record AdbDeviceEntry(string Serial, string State, string? Model)
{
    public DeviceTransport Transport => AdbOutputParser.IsNetworkSerial(Serial) ? DeviceTransport.Wifi : DeviceTransport.Usb;

    /// <summary>"device" is the only state in which adb accepts commands.</summary>
    public bool IsReady => State == "device";

    public bool IsThor => AdbOutputParser.IsThorModel(Model);
}

/// <summary>Pure parsing of adb output, kept separate so it can be tested with recorded output.</summary>
public static partial class AdbOutputParser
{
    /// <summary>
    /// Parses <c>adb devices -l</c>, e.g.
    /// <c>c26efaeb   device usb:1-2 product:kalama model:AYN_Thor device:kalama transport_id:11</c>.
    /// </summary>
    public static IReadOnlyList<AdbDeviceEntry> ParseDevices(string output)
    {
        var devices = new List<AdbDeviceEntry>();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices") || line.StartsWith('*'))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var model = parts.Skip(2).FirstOrDefault(p => p.StartsWith("model:"))?["model:".Length..];
            devices.Add(new AdbDeviceEntry(parts[0], parts[1], model));
        }

        return devices;
    }

    /// <summary>
    /// Extracts ip:port endpoints of devices announcing wireless debugging, from
    /// <c>adb mdns services</c> lines such as
    /// <c>adb-c26efaeb-AbCdEf  _adb-tls-connect._tcp  192.168.1.17:43321</c>.
    /// </summary>
    public static IReadOnlyList<string> ParseMdnsEndpoints(string output) =>
        output.Split('\n')
            .Where(line => line.Contains("_adb-tls-connect", StringComparison.Ordinal))
            .Select(line => EndpointRegex().Match(line))
            .Where(m => m.Success)
            .Select(m => m.Value)
            .Distinct()
            .ToList();

    /// <summary>Wi-Fi serials are ip:port or mDNS service names; USB serials are plain ids.</summary>
    public static bool IsNetworkSerial(string serial) =>
        serial.Contains(':') || serial.Contains("._adb-tls-connect.", StringComparison.Ordinal);

    /// <summary>adb reports "AYN_Thor" while getprop reports "AYN Thor".</summary>
    public static bool IsThorModel(string? model) =>
        model is not null && model.Replace('_', ' ').Contains("Thor", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when adb itself (not the shell command) says the target device is gone,
    /// e.g. "adb: device 'x' not found", "error: device offline", "error: closed".
    /// </summary>
    public static bool IsDeviceLost(string standardError) =>
        standardError.Split('\n')
            .Select(l => l.Trim().ToLowerInvariant())
            .Any(l => (l.StartsWith("adb:") || l.StartsWith("error:"))
                      && (l.Contains("not found") || l.Contains("offline") || l.Contains("closed")));

    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{1,3}){3}:\d{1,5}\b")]
    private static partial Regex EndpointRegex();
}
