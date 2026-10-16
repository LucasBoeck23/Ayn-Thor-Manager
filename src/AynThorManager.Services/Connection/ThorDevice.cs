namespace AynThorManager.Services.Connection;

public enum DeviceTransport
{
    Usb,
    Wifi
}

public sealed record ThorDevice(string Serial, DeviceTransport Transport, string Model);
