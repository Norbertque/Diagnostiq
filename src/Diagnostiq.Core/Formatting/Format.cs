using System.Globalization;

namespace Diagnostiq.Core.Formatting;

/// <summary>User-facing number and date formats. The UI is English, so formats are fixed rather than per-locale.</summary>
public static class Format
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>Memory sizes are binary: 17,179,869,184 bytes → "16 GB".</summary>
    public static string Memory(long bytes)
    {
        double gb = bytes / (double)(1L << 30);
        return gb >= 1 ? $"{gb.ToString(gb >= 10 || gb % 1 < 0.05 ? "0" : "0.#", En)} GB" : $"{bytes >> 20} MB";
    }

    /// <summary>Drive sizes are decimal, like on the box: 500,107,862,016 bytes → "500 GB".</summary>
    public static string Disk(long bytes) =>
        bytes >= 999_500_000_000 ? $"{(bytes / 1e12).ToString("0.#", En)} TB" : $"{(bytes / 1e9).ToString("0", En)} GB";

    public static string Date(DateTime? d) => d?.ToString("d MMM yyyy", En) ?? "";

    public static string MonthYear(DateTime? d) => d?.ToString("MMM yyyy", En) ?? "";

    public static string Number(long n) => n.ToString("N0", En);

    /// <summary>Joins the non-empty parts with a middle dot.</summary>
    public static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    public static string Minutes(TimeSpan t) => t.TotalMinutes < 1.5 ? "1 minute" : $"{Math.Round(t.TotalMinutes):0} minutes";
}
