using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Diagnostiq.Core.Hardware;

/// <param name="Port">The laptop's physical port, from the device's location path (same key for anything behind a hub on that port).</param>
public sealed record UsbArrival(string InstanceId, string Name, string Port, bool NewPort);

/// <summary>
/// Notices USB devices plugged in after it started and which laptop port each one used, by
/// polling the present USB devices through SetupAPI (no window or admin needed).
/// </summary>
public sealed partial class UsbPortWatcher : IDisposable
{
    private readonly UsbPortTracker _tracker;
    private readonly Timer _timer;
    private int _busy;

    /// <summary>Raised on a thread-pool thread.</summary>
    public event Action<UsbArrival>? Arrived;

    public UsbPortWatcher()
    {
        _tracker = new UsbPortTracker(PresentDevices());   // webcam, Bluetooth, fingerprint reader…
        _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public int PortCount => _tracker.PortCount;

    private void Poll()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            foreach (var arrival in _tracker.Update(PresentDevices())) Arrived?.Invoke(arrival);
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>"PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)#USB(1)" → "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)".</summary>
    internal static string? PortOf(string? locationPath)
    {
        if (locationPath is null) return null;
        var m = RootPort().Match(locationPath);
        return m.Success ? m.Value : null;
    }

    [GeneratedRegex(@"^.*?#USBROOT\(\d+\)#USB\(\d+\)")] private static partial Regex RootPort();

    // ---------- SetupAPI ----------

    private static readonly Guid UsbDeviceInterface = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    private const int DigcfPresent = 0x02, DigcfDeviceInterface = 0x10;
    private const int SpdrpDeviceDesc = 0x00, SpdrpFriendlyName = 0x0C, SpdrpLocationPaths = 0x23;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public int Size;
        public Guid ClassGuid;
        public int DevInst;
        public nint Reserved;
    }

    internal static List<(string InstanceId, string Name, string? Port)> PresentDevices()
    {
        var list = new List<(string, string, string?)>();
        var guid = UsbDeviceInterface;
        nint set = SetupDiGetClassDevsW(ref guid, null, 0, DigcfPresent | DigcfDeviceInterface);
        if (set == -1) return list;
        try
        {
            var info = new SpDevInfoData { Size = Marshal.SizeOf<SpDevInfoData>() };
            var buffer = new byte[2048];
            var id = new byte[1024];   // UTF-16, 512 chars
            for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref info); i++)
            {
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, id.Length / 2, out int len)) continue;
                string instance = System.Text.Encoding.Unicode.GetString(id, 0, Math.Max(len - 1, 0) * 2);
                string name = Property(set, ref info, SpdrpFriendlyName, buffer) ?? Property(set, ref info, SpdrpDeviceDesc, buffer) ?? "USB device";
                string? path = Property(set, ref info, SpdrpLocationPaths, buffer);   // multi-string; first entry is the path
                list.Add((instance, name, PortOf(path)));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    private static string? Property(nint set, ref SpDevInfoData info, int property, byte[] buffer)
    {
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, buffer, buffer.Length, out int size) || size < 2) return null;
        var s = System.Text.Encoding.Unicode.GetString(buffer, 0, size);
        int end = s.IndexOf('\0');
        return end >= 0 ? s[..end] : s;
    }

    [LibraryImport("setupapi.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, nint hwnd, int flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInfo(nint set, int index, ref SpDevInfoData info);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInstanceIdW(nint set, ref SpDevInfoData info, [Out] byte[] id, int sizeInChars, out int required);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceRegistryPropertyW(nint set, ref SpDevInfoData info, int property, out int regType,
        [Out] byte[] buffer, int size, out int required);

    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(nint set);

    public void Dispose() => _timer.Dispose();
}

/// <summary>
/// The bookkeeping behind <see cref="UsbPortWatcher"/>, apart from SetupAPI so it can be tested.
/// A device counts once per port: USB sticks with a serial number keep the same instance id in
/// every port, and the step asks the user to move the same stick from port to port.
/// </summary>
internal sealed class UsbPortTracker
{
    private readonly HashSet<string> _baseline;   // plugged in before the step: counts only after a replug
    private readonly HashSet<string> _seen = [];
    private readonly HashSet<string> _ports = [];

    public UsbPortTracker(IEnumerable<(string InstanceId, string Name, string? Port)> atStart) =>
        _baseline = [.. atStart.Select(Key)];

    public int PortCount { get { lock (_ports) return _ports.Count; } }

    /// <summary>Takes the devices present at one poll and returns what arrived since the last one.</summary>
    public List<UsbArrival> Update(IReadOnlyCollection<(string InstanceId, string Name, string? Port)> present)
    {
        // Forget unplugged devices, so plugging one back in, in this port or another, counts again.
        var keys = present.Select(Key).ToHashSet();
        _baseline.IntersectWith(keys);
        _seen.IntersectWith(keys);

        var arrivals = new List<UsbArrival>();
        foreach (var d in present)
        {
            if (d.Port is null || _baseline.Contains(Key(d)) || !_seen.Add(Key(d))) continue;
            bool isNew;
            lock (_ports) isNew = _ports.Add(d.Port);
            arrivals.Add(new UsbArrival(d.InstanceId, d.Name, d.Port, isNew));
        }
        return arrivals;
    }

    private static string Key((string InstanceId, string Name, string? Port) d) => $"{d.InstanceId}|{d.Port}";
}
