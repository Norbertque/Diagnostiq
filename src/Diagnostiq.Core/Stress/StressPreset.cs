namespace Diagnostiq.Core.Stress;

/// <summary>How long the combined CPU + memory + disk burn-in runs in Automatic mode.</summary>
public enum StressPreset { Quick, Standard, Extended }

public static class StressPresets
{
    /// <summary>Rough time for the hands-on slides plus the disk benchmark, for the "about N min" estimate.</summary>
    public static readonly TimeSpan HandsOnEstimate = TimeSpan.FromMinutes(6);

    public static TimeSpan Duration(this StressPreset preset) => preset switch
    {
        StressPreset.Quick => TimeSpan.FromMinutes(2),
        StressPreset.Extended => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromMinutes(5),
    };
}
