using ManagedNativeWifi;

namespace Diagnostiq.Core.Network;

/// <param name="HardwareSwitchOn">False when a physical switch or Fn key has the radio off.</param>
/// <param name="Standard">Link standard, e.g. "Wi-Fi 6 (802.11ax)".</param>
/// <param name="BandsSeen">Bands of the networks in range ("2.4 GHz", "5 GHz", "6 GHz"); shows what the card can receive.</param>
/// <param name="NamesHidden">Windows withholds network names because Location access for desktop apps is off.</param>
public sealed record WifiInfo(
    bool AdapterPresent,
    string? AdapterName,
    bool? RadioOn,
    bool? HardwareSwitchOn,
    bool Connected,
    string? Ssid,
    int? SignalPercent,
    string? Standard,
    int? LinkMbps,
    int NetworksVisible,
    IReadOnlyList<string> BandsSeen,
    bool NamesHidden);

/// <summary>Wi-Fi through the native WLAN API: locale-independent, unlike parsing netsh output.</summary>
public static class WifiProbe
{
    public static WifiInfo Read()
    {
        var iface = NativeWifi.EnumerateInterfaces()
            .OrderBy(i => i.Description.Contains("USB", StringComparison.OrdinalIgnoreCase))   // built-in card first
            .FirstOrDefault();
        if (iface is null) return new WifiInfo(false, null, null, null, false, null, null, null, null, 0, [], false);

        bool? radioOn = null, hardwareOn = null;
        if (NativeWifi.GetRadio(iface.Id) is { RadioStates.Count: > 0 } radio)
        {
            hardwareOn = radio.RadioStates.All(s => s.IsHardwareOn);
            radioOn = radio.RadioStates.Any(s => s.IsOn);
        }

        bool connected = iface.State == InterfaceState.Connected;
        string? ssid = null;
        int? signal = null, mbps = null;
        string? standard = null;
        if (connected)
        {
            var (result, conn) = NativeWifi.GetCurrentConnection(iface.Id);
            if (result == ActionResult.Success && conn is not null)
            {
                ssid = conn.Ssid?.ToString() is { Length: > 0 } s ? s : null;
                signal = conn.SignalQuality;
                mbps = Math.Max(conn.RxRate, conn.TxRate) / 1000;   // kbps
                standard = StandardName(conn.PhyType);
            }
        }

        var (bssResult, bss) = NativeWifi.EnumerateBssNetworks(iface.Id);
        var networks = bssResult == ActionResult.Success && bss is not null ? bss.ToList() : [];
        bool hidden = bssResult != ActionResult.Success || (connected && ssid is null);

        var bands = networks.Select(n => BandName(n.Band)).OfType<string>().Distinct().Order().ToList();
        int visible = networks.Select(n => n.Ssid?.ToString()).Where(s => !string.IsNullOrEmpty(s)).Distinct().Count();

        return new WifiInfo(true, iface.Description, radioOn, hardwareOn, connected, ssid, signal, standard, mbps,
            hidden && visible == 0 ? networks.Count : visible, bands, hidden);
    }

    /// <summary>Asks the card for a fresh scan (a few seconds), then reads the results.</summary>
    public static async Task<WifiInfo> ScanAsync(CancellationToken ct = default)
    {
        await NativeWifi.ScanNetworksAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        return Read();
    }

    internal static string? StandardName(PhyType phy) => phy switch
    {
        PhyType.Eht => "Wi-Fi 7 (802.11be)",
        PhyType.He => "Wi-Fi 6 (802.11ax)",
        PhyType.Vht => "Wi-Fi 5 (802.11ac)",
        PhyType.Ht => "Wi-Fi 4 (802.11n)",
        PhyType.Erp => "802.11g",
        PhyType.Ofdm => "802.11a",
        PhyType.HrDsss => "802.11b",
        _ => null,
    };

    internal static string? BandName(float ghz) => ghz switch
    {
        >= 2.3f and < 2.6f => "2.4 GHz",
        >= 4.9f and < 5.9f => "5 GHz",
        >= 5.9f and < 7.2f => "6 GHz",
        _ => null,
    };
}
