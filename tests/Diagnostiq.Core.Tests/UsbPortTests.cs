using Diagnostiq.Core.Hardware;

namespace Diagnostiq.Core.Tests;

public class UsbPortTests
{
    [Theory]
    [InlineData("PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)", "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)")]
    [InlineData("PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)#USB(1)", "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)")]   // behind a hub on port 2
    [InlineData("ACPI(_SB_)#ACPI(PCI0)", null)]
    [InlineData(null, null)]
    public void Port_is_the_root_hub_port(string? path, string? expected) => Assert.Equal(expected, UsbPortWatcher.PortOf(path));

    [Fact]
    [Trait("Category", "Integration")]
    public void Lists_present_usb_devices()
    {
        // Internal webcams, Bluetooth radios and fingerprint readers are USB devices on almost every laptop.
        var devices = UsbPortWatcher.PresentDevices();
        Assert.NotEmpty(devices);
        Assert.All(devices, d => Assert.False(string.IsNullOrEmpty(d.InstanceId)));
        Assert.Contains(devices, d => d.Port is not null);
    }
}
