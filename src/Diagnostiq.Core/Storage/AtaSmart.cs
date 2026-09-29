using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Storage;

public sealed record SmartAttribute(byte Id, string Name, byte Current, byte Worst, byte Threshold, long Raw)
{
    public bool BelowThreshold => Threshold > 0 && Current > 0 && Current <= Threshold;
}

public sealed record SmartDrive(string Instance, bool PredictFailure, IReadOnlyList<SmartAttribute> Attributes)
{
    public IReadOnlyList<string> Problems => AtaSmart.Evaluate(this);
    public bool Healthy => Problems.Count == 0;
}

/// <summary>
/// Classic ATA SMART via MSStorageDriver_* (SATA/IDE drives; NVMe drives report nothing here,
/// their health comes from the storage reliability counters). Needs admin.
/// </summary>
public static class AtaSmart
{
    private static readonly Dictionary<byte, string> Names = new()
    {
        [0x01] = "Read error rate", [0x05] = "Reallocated sectors", [0x09] = "Power-on hours",
        [0x0A] = "Spin-up retries", [0x0C] = "Power cycles", [0xAA] = "Available reserved space",
        [0xAB] = "Program fail count", [0xAC] = "Erase fail count", [0xAD] = "Wear leveling count",
        [0xB1] = "Wear range delta", [0xB4] = "Unused reserved blocks", [0xB7] = "SATA downshift errors",
        [0xB8] = "End-to-end errors", [0xBB] = "Reported uncorrectable", [0xBC] = "Command timeouts",
        [0xBE] = "Airflow temperature", [0xC2] = "Temperature", [0xC4] = "Reallocation events",
        [0xC5] = "Current pending sectors", [0xC6] = "Offline uncorrectable", [0xC7] = "UDMA CRC errors",
        [0xE7] = "SSD life left", [0xE9] = "Media wearout indicator", [0xF1] = "Total host writes",
        [0xF2] = "Total host reads",
    };

    /// <summary>
    /// Parses the 512-byte SMART data block: 2-byte version, then 30 entries of 12 bytes
    /// (id, flags[2], current, worst, raw[6], reserved). Thresholds use the same layout
    /// with the threshold value at offset 1.
    /// </summary>
    public static List<SmartAttribute> Parse(byte[] data, byte[]? thresholds)
    {
        var thr = new Dictionary<byte, byte>();
        if (thresholds is not null)
            for (int i = 2; i + 12 <= thresholds.Length; i += 12)
                if (thresholds[i] != 0) thr[thresholds[i]] = thresholds[i + 1];

        var list = new List<SmartAttribute>();
        for (int i = 2; i + 12 <= data.Length; i += 12)
        {
            byte id = data[i];
            if (id == 0) continue;
            long raw = 0;
            for (int j = 0; j < 6; j++) raw |= (long)data[i + 5 + j] << (8 * j);
            list.Add(new SmartAttribute(id, Names.GetValueOrDefault(id, $"Attribute 0x{id:X2}"),
                data[i + 3], data[i + 4], thr.GetValueOrDefault(id), raw));
        }
        return list;
    }

    /// <summary>Human-readable problems; empty list means healthy.</summary>
    public static IReadOnlyList<string> Evaluate(SmartDrive drive)
    {
        var problems = new List<string>();
        if (drive.PredictFailure) problems.Add("Drive predicts its own failure");
        foreach (var a in drive.Attributes)
        {
            if (a.BelowThreshold) problems.Add($"{a.Name} below failure threshold");
            if (a.Id is 0x05 or 0xC5 or 0xC6 && a.Raw > 0) problems.Add($"{a.Name}: {a.Raw}");
            if (a.Id is 0xE7 or 0xE9 && a.Current is > 0 and < 10) problems.Add($"{a.Name}: {a.Current}% left");
        }
        return problems.Distinct().ToList();
    }

    public static IReadOnlyList<SmartDrive>? Read()
    {
        var status = Wmi.Query("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", Wmi.RootWmi)
            .ToDictionary(o => o.Str("InstanceName") ?? "", o => o.Bool("PredictFailure") ?? false);
        var thresholds = Wmi.Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictThresholds", Wmi.RootWmi)
            .ToDictionary(o => o.Str("InstanceName") ?? "", o => o["VendorSpecific"] as byte[]);
        var drives = Wmi.Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData", Wmi.RootWmi)
            .Select(o =>
            {
                var name = o.Str("InstanceName") ?? "";
                var attrs = o["VendorSpecific"] is byte[] data ? Parse(data, thresholds.GetValueOrDefault(name)) : [];
                return new SmartDrive(name, status.GetValueOrDefault(name), attrs);
            })
            .ToList();
        return drives.Count > 0 ? drives : null;
    }
}
