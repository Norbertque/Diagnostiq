using System.Net.NetworkInformation;
using System.Net.Sockets;
using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Network;

public enum AdapterKind { WiFi, Ethernet }

public sealed record NetworkAdapterInfo(AdapterKind Kind, string Name, string Description, bool IsUp,
    string? IPv4, string? Gateway, long SpeedBps, string? MacAddress)
{
    /// <summary>A USB dongle or dock port rather than the laptop's own hardware.</summary>
    public bool IsExternal => Description.Contains("USB", StringComparison.OrdinalIgnoreCase);

    /// <summary>Up with a real (non-APIPA) address, i.e. actually connected to a network.</summary>
    public bool IsConnected => IsUp && IPv4 is not null && !IPv4.StartsWith("169.254.", StringComparison.Ordinal);
}

public sealed record BluetoothInfo(bool RadioPresent, string? RadioName, bool? Working);

public static class NetworkProbe
{
    public static IReadOnlyList<NetworkAdapterInfo> ReadAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            AdapterKind? kind = ni.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => AdapterKind.WiFi,
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => AdapterKind.Ethernet,
                _ => null,
            };
            if (kind is null || IsVirtual(ni)) continue;

            var props = ni.GetIPProperties();
            var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var gw = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any))?.Address.ToString();
            var mac = ni.GetPhysicalAddress().ToString();
            list.Add(new NetworkAdapterInfo(kind.Value, ni.Name, ni.Description, ni.OperationalStatus == OperationalStatus.Up,
                ipv4, gw, ni.Speed > 0 ? ni.Speed : 0, mac.Length == 12 ? mac : null));
        }
        // Built-in, connected adapters first, so "best" per kind is the first match.
        return list.OrderBy(a => a.IsExternal).ThenByDescending(a => a.IsConnected).ThenByDescending(a => a.IsUp).ToList();
    }

    // Hyper-V, VPN, VirtualBox, WSL adapters report as Ethernet but aren't hardware ports, and
    // NDIS filter bindings ("…-WFP Native MAC Layer LightWeight Filter-0000", "QoS Packet
    // Scheduler") show up as extra copies of the real Wi-Fi/Ethernet adapter.
    private static bool IsVirtual(NetworkInterface ni)
    {
        var d = ni.Description;
        string[] markers = ["Hyper-V", "Virtual", "VPN", "TAP-", "WireGuard", "Tailscale", "VMware", "VirtualBox", "Loopback",
                            "WAN Miniport", "Bluetooth Device", "Filter", "Packet Scheduler", "Kernel Debug",
                            "UsbNcm", "Remote NDIS"];  // phone/dock USB tethering
        return markers.Any(m => d.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The Bluetooth radio is the Bluetooth-class device on the USB/PCI bus. Paired devices
    /// (headphones, mice) are also PNPClass=Bluetooth but enumerate under BTHENUM/BTHLE.
    /// </summary>
    public static BluetoothInfo ReadBluetooth()
    {
        var devices = Wmi.Query("SELECT Name, DeviceID, Service, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass='Bluetooth'");
        var radio = devices.FirstOrDefault(d =>
            (d.Str("DeviceID") ?? "") is var id &&
            (id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)
             || string.Equals(d.Str("Service"), "BTHUSB", StringComparison.OrdinalIgnoreCase)));
        if (radio is null) return new BluetoothInfo(false, null, null);
        return new BluetoothInfo(true, radio.Str("Name"), radio.Int("ConfigManagerErrorCode") == 0);
    }
}
