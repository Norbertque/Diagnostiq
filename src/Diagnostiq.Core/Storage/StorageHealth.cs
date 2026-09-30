using System.Management;
using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Storage;

public enum DriveHealthStatus { Healthy, Warning, Unhealthy, Unknown }

/// <param name="WearPercent">Share of the drive's rated write endurance already used (SSD/NVMe).</param>
public sealed record ReliabilityCounters(
    int? TemperatureC,
    int? WearPercent,
    long? PowerOnHours,
    long? ReadErrorsUncorrected,
    long? WriteErrorsUncorrected,
    long? StartStopCycles);

/// <param name="Counters">Null when the drive/driver doesn't expose them or the app isn't elevated.</param>
public sealed record DriveHealth(
    int DiskNumber,
    string Model,
    string MediaType,
    string BusType,
    long SizeBytes,
    DriveHealthStatus Health,
    IReadOnlyList<string> OperationalStatus,
    ReliabilityCounters? Counters,
    bool CountersNeedAdmin)
{
    public bool IsUsb => BusType == "USB";
}

public sealed record DriveVerdict(CheckState State, IReadOnlyList<string> Reasons);

/// <summary>
/// Drive health from Windows Storage Management (MSFT_PhysicalDisk and its reliability
/// counters). Unlike the ATA SMART WMI class this works for NVMe, which is what most
/// laptops since ~2018 have.
/// </summary>
public static class StorageHealth
{
    public static IReadOnlyList<DriveHealth> Read()
    {
        var list = new List<DriveHealth>();
        foreach (var d in Wmi.Query("SELECT * FROM MSFT_PhysicalDisk", Wmi.Storage))
        {
            if (!int.TryParse(d.Str("DeviceId"), out var number)) continue;
            var (counters, needAdmin) = ReadCounters(d as ManagementObject);
            list.Add(new DriveHealth(
                DiskNumber: number,
                Model: d.Str("FriendlyName") ?? d.Str("Model") ?? "Unknown drive",
                MediaType: d.Int("MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unknown" },
                BusType: BusName(d.Int("BusType")),
                SizeBytes: d.Long("Size") ?? 0,
                Health: d.Int("HealthStatus") switch
                {
                    0 => DriveHealthStatus.Healthy,
                    1 => DriveHealthStatus.Warning,
                    2 => DriveHealthStatus.Unhealthy,
                    _ => DriveHealthStatus.Unknown,
                },
                OperationalStatus: d["OperationalStatus"] is ushort[] codes ? codes.Select(OperationalName).ToList() : [],
                Counters: counters,
                CountersNeedAdmin: needAdmin));
        }
        return list.OrderBy(x => x.DiskNumber).ToList();
    }

    private static (ReliabilityCounters?, bool NeedAdmin) ReadCounters(ManagementObject? disk)
    {
        if (disk is null) return (null, false);
        try
        {
            using var related = disk.GetRelated("MSFT_StorageReliabilityCounter");
            var c = related.Cast<ManagementBaseObject>().FirstOrDefault();
            if (c is null) return (null, !Probe.IsAdmin);   // standard users get an empty result, not "access denied"
            return (new ReliabilityCounters(
                TemperatureC: c.Int("Temperature") is > 0 and var t ? t : null,
                WearPercent: c.Int("Wear"),
                PowerOnHours: c.Long("PowerOnHours"),
                ReadErrorsUncorrected: c.Long("ReadErrorsUncorrected"),
                WriteErrorsUncorrected: c.Long("WriteErrorsUncorrected"),
                StartStopCycles: c.Long("StartStopCycleCount")), false);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied) { return (null, true); }
        catch (ManagementException) { return (null, false); }
        catch (UnauthorizedAccessException) { return (null, true); }
    }

    public static DriveVerdict Evaluate(DriveHealth d)
    {
        var fails = new List<string>();
        var warns = new List<string>();

        if (d.Health == DriveHealthStatus.Unhealthy) fails.Add("Windows reports the drive as unhealthy.");
        else if (d.Health == DriveHealthStatus.Warning) warns.Add("Windows reports a warning for this drive.");

        if (d.OperationalStatus.Contains("Predictive failure")) fails.Add("The drive predicts its own failure.");
        if (d.OperationalStatus.Any(s => s is "Error" or "Non-recoverable error")) fails.Add("The drive reports an error state.");
        if (d.OperationalStatus.Contains("Degraded")) warns.Add("The drive reports a degraded state.");

        if (d.Counters is { } c)
        {
            if (c.WearPercent >= 100) fails.Add($"Rated write endurance used up ({c.WearPercent}%).");
            else if (c.WearPercent >= 80) warns.Add($"{c.WearPercent}% of rated write endurance used.");
            long uncorrected = (c.ReadErrorsUncorrected ?? 0) + (c.WriteErrorsUncorrected ?? 0);
            if (uncorrected > 0) warns.Add($"{uncorrected} uncorrected read/write errors logged.");
            if (c.TemperatureC >= 70) warns.Add($"Running hot ({c.TemperatureC} °C).");
        }

        if (fails.Count > 0) return new(CheckState.Fail, [.. fails, .. warns]);
        if (warns.Count > 0) return new(CheckState.Warn, warns);
        if (d.Health == DriveHealthStatus.Unknown && d.Counters is null) return new(CheckState.Unknown, ["The drive doesn't report its health."]);
        return new(CheckState.Pass, []);
    }

    internal static string BusName(int? bus) => bus switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 11 => "SATA",
        12 => "SD", 13 => "MMC", 15 => "File-backed virtual", 16 => "Storage Spaces", 17 => "NVMe", 18 => "SCM", 19 => "UFS",
        _ => "Other",
    };

    internal static string OperationalName(ushort code) => code switch
    {
        2 => "OK", 3 => "Degraded", 4 => "Stressed", 5 => "Predictive failure", 6 => "Error", 7 => "Non-recoverable error",
        8 => "Starting", 9 => "Stopping", 10 => "Stopped", 11 => "In service", 12 => "No contact", 13 => "Lost communication",
        _ => $"Status {code}",
    };
}
