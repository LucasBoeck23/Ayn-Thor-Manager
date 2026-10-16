namespace AynThorManager.Services.Connection;

public enum DeviceTransport
{
    Usb,
    Wifi
}

/// <summary>
/// The device the app is currently talking to. <see cref="Serial"/> is the exact
/// adb serial (USB serial or ip:port) and is passed as <c>-s</c> on every command.
/// </summary>
public sealed record ThorDevice(string Serial, DeviceTransport Transport, string Model);
