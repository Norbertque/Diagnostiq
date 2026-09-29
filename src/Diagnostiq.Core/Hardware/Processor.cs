using System.Text.RegularExpressions;
using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Hardware;

public sealed record CpuInfo(string Name, string Manufacturer, int Cores, int Threads, int MaxClockMHz, int Sockets);

public sealed record MemoryModule(long CapacityBytes, int? SpeedMHz, string? Manufacturer, string? PartNumber, string? Slot);

public sealed record MemoryInfo(long InstalledBytes, long VisibleBytes, IReadOnlyList<MemoryModule> Modules)
{
    public double InstalledGB => InstalledBytes / 1024d / 1024 / 1024;
}

public static partial class ProcessorProbe
{
    public static CpuInfo? ReadCpu()
    {
        var cpus = Wmi.Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        if (cpus.Count == 0) return null;
        var first = cpus[0];
        return new CpuInfo(
            Name: CollapseSpaces(first.Str("Name") ?? "Unknown processor"),
            Manufacturer: first.Str("Manufacturer") ?? "",
            Cores: cpus.Sum(c => c.Int("NumberOfCores") ?? 0),
            Threads: cpus.Sum(c => c.Int("NumberOfLogicalProcessors") ?? 0),
            MaxClockMHz: first.Int("MaxClockSpeed") ?? 0,
            Sockets: cpus.Count);
    }

    public static MemoryInfo? ReadMemory()
    {
        var modules = Wmi.Query("SELECT Capacity, ConfiguredClockSpeed, Speed, Manufacturer, PartNumber, DeviceLocator FROM Win32_PhysicalMemory")
            .Select(m => new MemoryModule(
                CapacityBytes: m.Long("Capacity") ?? 0,
                SpeedMHz: m.Int("ConfiguredClockSpeed") is > 0 and var c ? c : m.Int("Speed"),
                Manufacturer: m.Str("Manufacturer"),
                PartNumber: m.Str("PartNumber"),
                Slot: m.Str("DeviceLocator")))
            .ToList();
        var visible = Wmi.First("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")?.Long("TotalPhysicalMemory") ?? 0;
        long installed = modules.Sum(m => m.CapacityBytes);
        if (installed == 0 && visible == 0) return null;
        return new MemoryInfo(installed > 0 ? installed : visible, visible, modules);
    }

    private static string CollapseSpaces(string s) => MultipleSpaces().Replace(s, " ").Trim();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();
}
