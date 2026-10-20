using AynThorManager.Services.Connection;
using static AynThorManager.Services.Tests.Fakes.AdbOutputs;

namespace AynThorManager.Services.Tests.Connection;

public class AdbOutputParserTests
{
    [Fact]
    public void Parses_the_real_thor_device_list()
    {
        var devices = AdbOutputParser.ParseDevices(DevicesUsbAndWifi);

        Assert.Equal(2, devices.Count);
        Assert.Equal(new AdbDeviceEntry(UsbSerial, "device", "AYN_Thor"), devices[0]);
        Assert.Equal(new AdbDeviceEntry(WifiSerial, "device", "AYN_Thor"), devices[1]);
        Assert.Equal(DeviceTransport.Usb, devices[0].Transport);
        Assert.Equal(DeviceTransport.Wifi, devices[1].Transport);
        Assert.All(devices, d => Assert.True(d.IsReady));
    }

    [Fact]
    public void Empty_list_and_daemon_banner_produce_no_devices()
    {
        Assert.Empty(AdbOutputParser.ParseDevices(DevicesEmpty));
        Assert.Empty(AdbOutputParser.ParseDevices(DaemonStarting + DevicesEmpty));
        Assert.Empty(AdbOutputParser.ParseDevices(""));
    }

    [Fact]
    public void Tab_separated_output_without_l_flag_is_parsed()
    {
        var devices = AdbOutputParser.ParseDevices("List of devices attached\n192.168.1.17:43321\tdevice\n\n");

        Assert.Equal(new AdbDeviceEntry(WifiSerial, "device", null), Assert.Single(devices));
    }

    [Theory]
    [InlineData("unauthorized")]
    [InlineData("offline")]
    [InlineData("authorizing")]
    [InlineData("connecting")]
    public void Non_ready_states_are_kept_but_not_ready(string state)
    {
        var device = Assert.Single(AdbOutputParser.ParseDevices(Devices(WifiLine(WifiSerial, state))));

        Assert.Equal(state, device.State);
        Assert.False(device.IsReady);
        Assert.Null(device.Model);
    }

    [Theory]
    [InlineData("192.168.1.17:43321", DeviceTransport.Wifi)]
    [InlineData("adb-c26efaeb-AbCdEf._adb-tls-connect._tcp", DeviceTransport.Wifi)]
    [InlineData("c26efaeb", DeviceTransport.Usb)]
    [InlineData("emulator-5554", DeviceTransport.Usb)]
    public void Transport_is_derived_from_the_serial(string serial, DeviceTransport expected) =>
        Assert.Equal(expected, new AdbDeviceEntry(serial, "device", null).Transport);

    [Theory]
    [InlineData("adb: device '192.168.1.17:43321' not found\n", true)]
    [InlineData("error: device offline\n", true)]
    [InlineData("error: closed\n", true)]
    [InlineData("ls: /naoexiste: No such file or directory\n", false)]
    [InlineData("Command timed out after 2 seconds", false)]
    [InlineData("", false)]
    public void Detects_when_adb_lost_the_device(string stderr, bool expected) =>
        Assert.Equal(expected, AdbOutputParser.IsDeviceLost(stderr));
}
