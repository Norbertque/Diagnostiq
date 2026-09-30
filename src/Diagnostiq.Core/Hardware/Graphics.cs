using System.Management;
using Diagnostiq.Core.Interop;
using Diagnostiq.Core.Probing;
using Microsoft.Win32;

namespace Diagnostiq.Core.Hardware;

/// <param name="DedicatedMemoryBytes">Dedicated video memory; integrated GPUs report a small reserve and share system RAM.</param>
/// <param name="HasProblem">Device Manager shows an error for it (driver missing, stopped, …).</param>
public sealed record GpuInfo(string Name, string? DriverVersion, DateTime? DriverDate, long? DedicatedMemoryBytes, bool IsBasicDisplayDriver, bool HasProblem);

public static class GraphicsProbe
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static IReadOnlyList<GpuInfo> Read()
    {
        var vram = ReadDedicatedMemory();
        var list = new List<GpuInfo>();
        foreach (var gpu in Wmi.Query("SELECT Name, DriverVersion, DriverDate, AdapterRAM, ConfigManagerErrorCode, PNPDeviceID FROM Win32_VideoController"))
        {
            var name = gpu.Str("Name");
            if (name is null || IsVirtualAdapter(name, gpu.Str("PNPDeviceID"))) continue;

            DateTime? date = null;
            if (gpu.Str("DriverDate") is { } raw)
                try { date = ManagementDateTimeConverter.ToDateTime(raw).Date; } catch (ArgumentOutOfRangeException) { }

            // AdapterRAM is a UInt32 and tops out at 4 GB; the driver's registry value doesn't.
            long? memory = vram.TryGetValue(name, out var reg) ? reg : gpu.Long("AdapterRAM") is > 0 and var r ? r : null;
            list.Add(new GpuInfo(name, gpu.Str("DriverVersion"), date, memory,
                IsBasicDisplayDriver: name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase),
                HasProblem: gpu.Int("ConfigManagerErrorCode") is > 0));
        }
        return list;
    }

    /// <summary>Windows 11 needs DirectX 12 with a WDDM 2.0+ driver; D3D12 only loads on WDDM 2.0+, so one call covers both.</summary>
    public static bool SupportsDirectX12()
    {
        try
        {
            return Native.D3D12CreateDevice(IntPtr.Zero, Native.D3DFeatureLevel11_0, Native.IidID3D12Device, IntPtr.Zero) >= 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static bool IsVirtualAdapter(string name, string? pnpId) =>
        name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)
        || (pnpId?.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase) ?? false);  // software devices (Miracast, IDD)

    private static Dictionary<string, long> ReadDedicatedMemory()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var cls = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (cls is null) return result;
        foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            try
            {
                using var key = cls.OpenSubKey(sub);
                // Drivers store it as REG_QWORD or as 8 raw bytes.
                long? bytes = key?.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long q => q,
                    byte[] { Length: >= 8 } raw => BitConverter.ToInt64(raw),
                    _ => null,
                };
                if (key?.GetValue("DriverDesc") is string desc && bytes > 0)
                    result.TryAdd(desc, bytes.Value);
            }
            catch (System.Security.SecurityException) { }   // some instances are locked down
        }
        return result;
    }
}
