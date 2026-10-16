using System.Text.RegularExpressions;

namespace AynThorManager.Services.Connection;

public sealed record AdbDeviceEntry(string Serial, string State, string? Model)
{
    public DeviceTransport Transport => AdbOutputParser.IsNetworkSerial(Serial) ? DeviceTransport.Wifi : DeviceTransport.Usb;

    public bool IsReady => State == "device";

    public bool IsThor => AdbOutputParser.IsThorModel(Model);
}

public static partial class AdbOutputParser
{
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

    public static IReadOnlyList<string> ParseMdnsEndpoints(string output) =>
        output.Split('\n')
            .Where(line => line.Contains("_adb-tls-connect", StringComparison.Ordinal))
            .Select(line => EndpointRegex().Match(line))
            .Where(m => m.Success)
            .Select(m => m.Value)
            .Distinct()
            .ToList();

    public static bool IsNetworkSerial(string serial) =>
        serial.Contains(':') || serial.Contains("._adb-tls-connect.", StringComparison.Ordinal);

    public static bool IsThorModel(string? model) =>
        model is not null && model.Replace('_', ' ').Contains("Thor", StringComparison.OrdinalIgnoreCase);

    public static bool IsDeviceLost(string standardError) =>
        standardError.Split('\n')
            .Select(l => l.Trim().ToLowerInvariant())
            .Any(l => (l.StartsWith("adb:") || l.StartsWith("error:"))
                      && (l.Contains("not found") || l.Contains("offline") || l.Contains("closed")));

    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{1,3}){3}:\d{1,5}\b")]
    private static partial Regex EndpointRegex();
}
