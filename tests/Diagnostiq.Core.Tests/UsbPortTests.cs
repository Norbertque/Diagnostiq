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

    private const string Webcam = @"USB\VID_0BDA&PID_5520\200901010001";
    private const string Stick = @"USB\VID_0781&PID_5581\4C530001230518115223";   // serial number: same id in every port
    private const string PortA = "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(1)", PortB = "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)",
        PortC = "PCIROOT(0)#PCI(0D00)#USBROOT(0)#USB(1)", Internal = "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(7)";

    private static (string InstanceId, string Name, string? Port) Dev(string id, string port) => (id, "Device", port);

    private static readonly (string, string, string?)[] Nothing = [];

    [Fact]
    public void Same_stick_moved_across_three_ports_counts_three_ports()
    {
        var tracker = new UsbPortTracker([Dev(Webcam, Internal)]);
        var arrivals = new List<UsbArrival>();
        foreach (var port in new[] { PortA, PortB, PortC })
        {
            arrivals.AddRange(tracker.Update([Dev(Webcam, Internal), Dev(Stick, port)]));
            arrivals.AddRange(tracker.Update([Dev(Webcam, Internal)]));   // unplugged before the next port
        }
        Assert.Equal(3, tracker.PortCount);
        Assert.Equal([PortA, PortB, PortC], arrivals.Select(a => a.Port));
        Assert.All(arrivals, a => Assert.True(a.NewPort));
    }

    [Fact]
    public void Device_plugged_in_before_the_step_counts_only_after_a_replug()
    {
        var tracker = new UsbPortTracker([Dev(Stick, PortA)]);
        Assert.Empty(tracker.Update([Dev(Stick, PortA)]));
        Assert.Equal(0, tracker.PortCount);

        for (int i = 0; i < UsbPortTracker.BaselineMissesToForget; i++) Assert.Empty(tracker.Update(Nothing));
        var arrival = Assert.Single(tracker.Update([Dev(Stick, PortA)]));
        Assert.Equal(PortA, arrival.Port);
        Assert.Equal(1, tracker.PortCount);
    }

    [Fact]
    public void Built_in_device_missing_for_one_poll_isnt_counted_as_a_port()
    {
        // A Bluetooth radio reset or a camera privacy switch can make a built-in device vanish for a moment.
        var tracker = new UsbPortTracker([Dev(Webcam, Internal)]);
        Assert.Empty(tracker.Update(Nothing));
        Assert.Empty(tracker.Update([Dev(Webcam, Internal)]));
        Assert.Equal(0, tracker.PortCount);
    }

    [Fact]
    public void Replugging_in_the_same_port_doesnt_count_twice()
    {
        var tracker = new UsbPortTracker(Nothing);
        Assert.True(Assert.Single(tracker.Update([Dev(Stick, PortA)])).NewPort);
        Assert.Empty(tracker.Update([Dev(Stick, PortA)]));   // still plugged in: no new arrival

        Assert.Empty(tracker.Update(Nothing));
        Assert.False(Assert.Single(tracker.Update([Dev(Stick, PortA)])).NewPort);   // "Same port as before"
        Assert.Equal(1, tracker.PortCount);
    }

    [Fact]
    public void Devices_without_a_port_are_ignored()
    {
        var tracker = new UsbPortTracker(Nothing);
        Assert.Empty(tracker.Update([(Stick, "Device", null)]));
        Assert.Equal(0, tracker.PortCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Lists_present_usb_devices()
    {
        // Internal webcams, Bluetooth radios and fingerprint readers are USB devices on almost every laptop.
        var devices = UsbPortWatcher.PresentDevices();
        Assert.NotNull(devices);
        Assert.NotEmpty(devices);
        Assert.All(devices, d => Assert.False(string.IsNullOrEmpty(d.InstanceId)));
        Assert.Contains(devices, d => d.Port is not null);
    }
}
