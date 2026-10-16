namespace AynThorManager.Services.Tests.Fakes;

/// <summary>
/// adb output. Entries marked "recorded" were captured from the real AYN Thor
/// (adb on Debian, Thor on 192.168.1.17 with wireless debugging on port 43321).
/// The others follow adb's documented formats for states we could not reproduce
/// without touching the device.
/// </summary>
internal static class AdbOutputs
{
    public const string UsbSerial = "c26efaeb";
    public const string ThorIp = "192.168.1.17";
    public const string WifiSerial = "192.168.1.17:43321";

    // recorded
    public const string DevicesUsbAndWifi =
        "List of devices attached\n" +
        "c26efaeb               device usb:1-2 product:kalama model:AYN_Thor device:kalama transport_id:11\n" +
        "192.168.1.17:43321     device product:kalama model:AYN_Thor device:kalama transport_id:9\n\n";

    public const string DevicesEmpty = "List of devices attached\n\n";

    public static string Devices(params string[] lines) =>
        "List of devices attached\n" + string.Concat(lines.Select(l => l + "\n")) + "\n";

    public static string WifiLine(string serial, string state = "device", string model = "AYN_Thor") =>
        state == "device"
            ? $"{serial}     device product:kalama model:{model} device:kalama transport_id:9"
            : $"{serial}     {state} transport_id:9";

    public static string UsbLine(string serial = UsbSerial, string state = "device") =>
        state == "device"
            ? $"{serial}               device usb:1-2 product:kalama model:AYN_Thor device:kalama transport_id:11"
            : $"{serial}               {state} usb:1-2 transport_id:11";

    // recorded
    public static string ConnectedTo(string serial) => $"connected to {serial}\n";
    public static string AlreadyConnected(string serial) => $"already connected to {serial}\n";
    public static string NoRouteToHost(string serial) => $"failed to connect to '{serial}': No route to host\n";
    public static string ConnectionRefused(string serial) => $"failed to connect to '{serial}': Connection refused\n";
    public static string DeviceNotFound(string serial) => $"adb: device '{serial}' not found\n";
    public const string LsNoSuchFile = "ls: /naoexiste: No such file or directory\n";
    public const string MdnsEmpty = "List of discovered mdns services\n\n";

    // documented formats (not reproducible without changing the device)
    public static string CannotConnect(string serial) => $"cannot connect to {serial}: Connection refused (111)\n";
    public static string FailedToAuthenticate(string serial) => $"failed to authenticate to {serial}\n";
    public const string DeviceOffline = "error: device offline\n";
    public const string Closed = "error: closed\n";
    public const string DaemonStarting =
        "* daemon not running; starting now at tcp:5037\n* daemon started successfully\n";

    public static string Mdns(params string[] lines) =>
        "List of discovered mdns services\n" + string.Concat(lines.Select(l => l + "\n")) + "\n";

    public static string MdnsConnect(string endpoint, string name = "adb-c26efaeb-AbCdEf") =>
        $"{name}\t_adb-tls-connect._tcp\t{endpoint}";

    public static string MdnsPairing(string endpoint, string name = "adb-c26efaeb-AbCdEf") =>
        $"{name}\t_adb-tls-pairing._tcp\t{endpoint}";
}
