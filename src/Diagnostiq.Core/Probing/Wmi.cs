using System.Management;

namespace Diagnostiq.Core.Probing;

/// <summary>Thin WMI helpers. Exceptions propagate so <see cref="Probe"/> can classify them.</summary>
internal static class Wmi
{
    public const string Cimv2 = @"root\CIMV2";
    public const string RootWmi = @"root\WMI";
    public const string Tpm = @"root\CIMV2\Security\MicrosoftTpm";
    public const string Storage = @"root\Microsoft\Windows\Storage";

    public static List<ManagementBaseObject> Query(string wql, string scope = Cimv2)
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        return results.Cast<ManagementBaseObject>().ToList();
    }

    public static ManagementBaseObject? First(string wql, string scope = Cimv2) => Query(wql, scope).FirstOrDefault();

    public static string? Str(this ManagementBaseObject o, string prop)
    {
        var s = o[prop]?.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public static long? Long(this ManagementBaseObject o, string prop) =>
        o[prop] is { } v ? Convert.ToInt64(v) : null;

    public static int? Int(this ManagementBaseObject o, string prop) =>
        o[prop] is { } v ? Convert.ToInt32(v) : null;

    public static bool? Bool(this ManagementBaseObject o, string prop) =>
        o[prop] is { } v ? Convert.ToBoolean(v) : null;
}
