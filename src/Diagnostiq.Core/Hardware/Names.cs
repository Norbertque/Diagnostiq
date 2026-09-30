using System.Text.RegularExpressions;

namespace Diagnostiq.Core.Hardware;

public static partial class Names
{
    /// <summary>"Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz" → "Intel Core i5-8250U".</summary>
    public static string Clean(string raw)
    {
        var s = Trademarks().Replace(raw, " ");
        s = ClockSuffix().Replace(s, " ");
        s = CpuWord().Replace(s, " ");
        return Spaces().Replace(s, " ").Trim();
    }

    [GeneratedRegex(@"\((R|TM|C)\)|®|™", RegexOptions.IgnoreCase)] private static partial Regex Trademarks();
    [GeneratedRegex(@"@\s*[\d.]+\s*GHz", RegexOptions.IgnoreCase)] private static partial Regex ClockSuffix();
    [GeneratedRegex(@"\bCPU\b", RegexOptions.IgnoreCase)] private static partial Regex CpuWord();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
