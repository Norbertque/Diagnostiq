using System.Text.RegularExpressions;
using Diagnostiq.Core.Hardware;

namespace Diagnostiq.Core.Win11;

public enum CpuSupport
{
    Supported,
    /// <summary>Newer than the lists we ship (e.g. a future Core Ultra series); very likely fine.</summary>
    LikelySupported,
    NotSupported,
    /// <summary>Name not recognised (virtual CPU, unusual vendor string).</summary>
    Unknown,
}

public sealed record CpuSupportResult(CpuSupport Support, string Family);

/// <summary>
/// Matches a CPU name against Microsoft's published Windows 11 processor lists
/// (learn.microsoft.com/windows-hardware/design/minimum/supported/windows-11-*-supported-*-processors).
/// Intel is published per series in every list version, so it is matched by rules; AMD and
/// Qualcomm are matched per model (21H2/22H2/24H2 lists) plus the 25H2 series for newer families.
/// A CPU on any list version stays eligible to upgrade, so the lists are merged.
/// </summary>
public static partial class SupportedCpus
{
    /// <summary>When the lists were last updated by Microsoft / reviewed for this build. Reads on after "checked against".</summary>
    public const string ListDate = "Microsoft's lists (updated October 2025, last reviewed September 2026)";

    private static readonly Lazy<HashSet<string>> AmdModels = new(LoadAmdModels);

    public static CpuSupportResult Check(string cpuName)
    {
        var name = Clean(cpuName);
        if (name.Length == 0) return new(CpuSupport.Unknown, "Unknown processor");

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return CheckIntel(name);
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Athlon", StringComparison.OrdinalIgnoreCase) || name.Contains("EPYC", StringComparison.OrdinalIgnoreCase))
            return CheckAmd(name);
        if (name.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase) || name.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Microsoft SQ", StringComparison.OrdinalIgnoreCase))
            return CheckQualcomm(name);
        return new(CpuSupport.Unknown, name);
    }

    /// <summary>"12th Gen Intel(R) Core(TM) i7-1265U CPU @ 1.80GHz" → "12th Gen Intel Core i7-1265U".</summary>
    internal static string Clean(string raw) =>
        Regex.Replace(Names.Clean(raw), @"\s*\bProcessor\b", "", RegexOptions.IgnoreCase).Trim();

    // ---------- Intel (series rules mirroring the published list) ----------

    private static CpuSupportResult CheckIntel(string name)
    {
        // Core Ultra (Series 1/2/3): "Core Ultra 7 155H", "Core Ultra 7 258V", "Core Ultra X7 358H".
        if (CoreUltra().Match(name) is { Success: true } ultra)
        {
            int series = ultra.Groups["n"].Value[0] - '0';
            return series <= 3 ? new(CpuSupport.Supported, $"Intel Core Ultra (Series {series})")
                               : new(CpuSupport.LikelySupported, $"Intel Core Ultra (Series {series})");
        }

        // Core i3/i5/i7/i9 and Core m: generation from the model number.
        if (CoreI().Match(name) is { Success: true } core)
        {
            var digits = core.Groups["num"].Value;
            var suffix = core.Groups["suffix"].Value;
            int gen = digits.Length == 5 || (digits.Length == 4 && digits[0] == '1') ? int.Parse(digits[..2]) : digits[0] - '0';
            if (digits.Length == 3) gen = 1;
            string family = $"Intel Core {core.Groups["tier"].Value} ({Ordinal(gen)} Gen)";

            if (gen is >= 8 and <= 14) return new(CpuSupport.Supported, family);
            if (gen == 7 && suffix.StartsWith('X')) return new(CpuSupport.Supported, "Intel Core X-series (7000X)");
            if (gen > 14) return new(CpuSupport.LikelySupported, family);
            return new(CpuSupport.NotSupported, family);
        }

        // Core 3/5/7 without "i" (Series 1: 1xx, Series 2: 2xx): "Core 7 150U".
        if (CoreSeries().Match(name) is { Success: true } cs)
        {
            int series = cs.Groups["n"].Value[0] - '0';
            return series <= 2 ? new(CpuSupport.Supported, $"Intel Core (Series {series})")
                               : new(CpuSupport.LikelySupported, $"Intel Core (Series {series})");
        }

        // Core i3-N300/N305.
        if (Regex.IsMatch(name, @"\bi3-N3\d\d\b")) return new(CpuSupport.Supported, "Intel Core N300 series");

        // "Intel N100", "Intel Processor N200", "N95/N97" (N90 series), "Processor U300", "Intel 300".
        if (Regex.Match(name, @"\b(?<p>[NU])(?<n>\d{2,3})E?\b") is { Success: true } np && !name.Contains("Celeron") && !name.Contains("Pentium"))
        {
            var p = np.Groups["p"].Value;
            var n = np.Groups["n"].Value;
            bool listed = p == "N" && (n.Length == 3 ? n[0] is '1' or '2' : n[0] == '9') || p == "U" && n.Length == 3 && n[0] == '3';
            if (listed) return new(CpuSupport.Supported, $"Intel Processor {p}{n}");
        }
        if (Regex.IsMatch(name, @"\bIntel (Processor )?300T?\b")) return new(CpuSupport.Supported, "Intel Processor 300");

        // Celeron: 3000–7000 (no letter), G4000–G6000, J4000, N4000, N5000.
        if (Regex.Match(name, @"Celeron\s+(?<p>[GJN]?)(?<n>\d{4})") is { Success: true } cel)
        {
            var p = cel.Groups["p"].Value;
            char d = cel.Groups["n"].Value[0];
            bool listed = p switch
            {
                "" => d is >= '3' and <= '7',
                "G" => d is >= '4' and <= '6',
                "J" => d == '4',
                "N" => d is '4' or '5',
                _ => false,
            };
            return new(listed ? CpuSupport.Supported : CpuSupport.NotSupported, $"Intel Celeron {p}{d}000 series");
        }

        // Pentium: Gold 4000U/Y, 5000–8000, G5000, G7000; Silver J5000, N6000; 6800.
        if (Regex.Match(name, @"Pentium\s+(?:(?<tier>Gold|Silver)\s+)?(?<p>[GJN]?)(?<n>\d{4})(?<s>[A-Z]?)") is { Success: true } pen)
        {
            var p = pen.Groups["p"].Value;
            var n = pen.Groups["n"].Value;
            var s = pen.Groups["s"].Value;
            char d = n[0];
            bool listed = p switch
            {
                "" => d == '4' ? s is "U" or "Y" : d is >= '5' and <= '8',   // includes 6800 series
                "G" => d is '5' or '7',
                "J" => d == '5',
                "N" => d == '6',
                _ => false,
            };
            return new(listed ? CpuSupport.Supported : CpuSupport.NotSupported, $"Intel Pentium {p}{d}000 series");
        }

        if (Regex.IsMatch(name, @"Atom\s+x7\d{3}", RegexOptions.IgnoreCase)) return new(CpuSupport.Supported, "Intel Atom x7000 series");
        if (name.Contains("Atom", StringComparison.OrdinalIgnoreCase)) return new(CpuSupport.NotSupported, "Intel Atom");

        if (name.Contains("Xeon", StringComparison.OrdinalIgnoreCase)) return CheckXeon(name);

        // Anything else branded Core/Celeron/Pentium predates the list ("Core i7 920", "Core2 Duo T7700", "m3-7Y30").
        if (Regex.Match(name, @"\b(Core|Celeron|Pentium)", RegexOptions.IgnoreCase) is { Success: true } old)
            return new(CpuSupport.NotSupported, $"Intel {old.Value} (older generation)");
        return new(CpuSupport.Unknown, name);
    }

    private static CpuSupportResult CheckXeon(string name)
    {
        // Mobile and workstation parts show up in laptops: W-10855M, W-11955M, E-2176M.
        if (Regex.Match(name, @"\b(?<s>[WED])-(?<n>\d{4,5})") is { Success: true } x)
        {
            var s = x.Groups["s"].Value;
            var n = x.Groups["n"].Value;
            bool listed = s switch
            {
                "W" => n.Length == 5 ? n[..2] is "10" or "11" : n[..2] is "12" or "13" or "21" or "22" or "24" or "25" or "31" or "33" or "34" or "35",
                "E" => n.Length == 4 && n[0] == '2' && n[1] is >= '1' and <= '4',
                "D" => n.Length == 4 && n[..2] is "17" or "18" or "27" or "28",
                _ => false,
            };
            return new(listed ? CpuSupport.Supported : CpuSupport.NotSupported, $"Intel Xeon {s}-{n}");
        }
        // Scalable (Bronze/Silver/Gold/Platinum 1st–5th gen) and Xeon 600 workstation.
        if (Regex.IsMatch(name, @"Xeon\s+(Bronze|Silver|Gold|Platinum|w\d-)", RegexOptions.IgnoreCase))
            return new(CpuSupport.Supported, "Intel Xeon Scalable / W");
        return new(CpuSupport.NotSupported, "Intel Xeon (older)");
    }

    // ---------- AMD ----------

    private static CpuSupportResult CheckAmd(string name)
    {
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
            if (AmdModels.Value.Contains(t)) return new(CpuSupport.Supported, AmdFamily(name));

        // 25H2 series: Ryzen AI 300 / AI Max / AI Z2, Z1/Z2, 100/200 series, 4000–9000.
        if (Regex.IsMatch(name, @"Ryzen\s+(AI|Z\d)", RegexOptions.IgnoreCase)) return new(CpuSupport.Supported, AmdFamily(name));
        if (Regex.Match(name, @"Ryzen\s+(?<tr>Threadripper\s+)?(PRO\s+)?(\d\s+)?(PRO\s+)?(?<n>\d{3,4})", RegexOptions.IgnoreCase) is { Success: true } r)
        {
            var n = r.Groups["n"].Value;
            if (r.Groups["tr"].Success && n.Length == 4 && n[0] == '3') return new(CpuSupport.Supported, AmdFamily(name));
            if (n.Length == 3 && n[0] is '1' or '2') return new(CpuSupport.Supported, AmdFamily(name));
            if (n.Length == 4 && n[0] is >= '4' and <= '9') return new(CpuSupport.Supported, AmdFamily(name));
            return new(CpuSupport.NotSupported, AmdFamily(name));
        }
        if (Regex.IsMatch(name, @"Athlon\s+(Gold\s+|Silver\s+)?7\d{3}U", RegexOptions.IgnoreCase)) return new(CpuSupport.Supported, "AMD Athlon 7000 U series");
        return new(CpuSupport.NotSupported, AmdFamily(name));
    }

    /// <summary>"AMD Ryzen 5 PRO 3500U w/ Radeon Vega Mobile Gfx" → "AMD Ryzen 5 PRO 3500U".</summary>
    private static string AmdFamily(string name)
    {
        var s = Regex.Replace(name, @"\s+(with|w/)\s.*$", "", RegexOptions.IgnoreCase).Trim();
        return s.StartsWith("AMD", StringComparison.OrdinalIgnoreCase) ? s : "AMD " + s;
    }

    private static HashSet<string> LoadAmdModels()
    {
        using var stream = typeof(SupportedCpus).Assembly.GetManifestResourceStream("win11-amd-models.txt")
            ?? throw new InvalidOperationException("win11-amd-models.txt resource missing");
        using var reader = new StreamReader(stream);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
            if (line.Length > 0 && line[0] != '#') set.Add(line.Trim());
        return set;
    }

    // ---------- Qualcomm ----------

    private static CpuSupportResult CheckQualcomm(string name)
    {
        // Snapdragon X (X1, X1P, X1E), 8cx / 8c / 7c families, 850, Microsoft SQ1–SQ3.
        if (Regex.IsMatch(name, @"\bX1[EP]?\d*|Snapdragon\s+X\b", RegexOptions.IgnoreCase)) return new(CpuSupport.Supported, "Qualcomm Snapdragon X");
        if (Regex.IsMatch(name, @"\b(8cx|8c|7c\+?|850)\b", RegexOptions.IgnoreCase)) return new(CpuSupport.Supported, "Qualcomm " + name);
        if (Regex.IsMatch(name, @"Microsoft SQ\d", RegexOptions.IgnoreCase)) return new(CpuSupport.Supported, "Microsoft SQ");
        return new(CpuSupport.NotSupported, name);
    }

    private static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

    [GeneratedRegex(@"Core\s+Ultra\s+X?\d\s+(?<n>\d)\d\d[A-Z]*", RegexOptions.IgnoreCase)] private static partial Regex CoreUltra();
    [GeneratedRegex(@"\b(?<tier>i[3579]|m[357])-(?<num>\d{3,5})(?<suffix>[A-Z]*\d?)\b")] private static partial Regex CoreI();
    [GeneratedRegex(@"\bCore\s+[3579]\s+(?<n>\d)\d\d[A-Z]*\b", RegexOptions.IgnoreCase)] private static partial Regex CoreSeries();
}
