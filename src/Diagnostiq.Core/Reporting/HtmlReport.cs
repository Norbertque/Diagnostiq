using System.Globalization;
using System.Net;
using System.Text;
using Diagnostiq.Core.Scoring;

namespace Diagnostiq.Core.Reporting;

/// <summary>One self-contained HTML file (no scripts, no external assets) that prints cleanly to A4/PDF.</summary>
public static class HtmlReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(ReportModel m)
    {
        var h = new StringBuilder();
        h.Append($"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E($"Laptop report: {m.Device.Name}{(m.Device.Serial is { } sn ? $" ({sn})" : "")}")}</title>
            <style>{Css}</style></head><body><main>
            """);

        // Header: what machine, when, overall verdict.
        string verdictClass = m.Health.Verdict switch { "Excellent" => "pass", "OK" => "warn", _ => "fail" };
        h.Append($"""
            <header>
              <div>
                <div class="eyebrow">Laptop check report</div>
                <h1>{E(m.Device.Name)}</h1>
                <div class="meta">{E(Join(m.Device.Serial is { } serial ? $"{m.Device.SerialLabel} {serial}" : null,
                    m.Device.ModelNumber is { } mn ? $"Type {mn}" : null,
                    m.Device.Bios is { } bios ? $"BIOS {bios}{(m.Device.BiosDate is { } bd ? $" ({bd})" : "")}" : null,
                    m.Created.ToString("d MMM yyyy, HH:mm", CultureInfo.GetCultureInfo("en-GB"))))}</div>
              </div>
              <div class="score {verdictClass}" aria-label="Health score {m.Health.Score} of 100">
                <div class="num">{m.Health.Score}</div><div class="of">of 100</div>
                <div class="chip {verdictClass}">{E(m.Health.Verdict)}</div>
              </div>
            </header>
            """);

        // Summary: what cost points, what wasn't covered.
        h.Append("<section><h2>Summary</h2>");
        if (m.Health.Deductions.Count == 0)
            h.Append("<p>No problems found in the tests that were run.</p>");
        else
        {
            h.Append("<table class=\"list\">");
            foreach (var d in m.Health.Deductions)
                h.Append($"<tr><td class=\"c\">{Chip(d.Severity switch { Severity.Critical => "Fail", Severity.Major => "Warn", _ => "Minor" }, d.Severity.ToString())}</td>" +
                         $"<td class=\"area\">{E(d.Area)}</td><td>{E(d.Reason)}</td><td class=\"pts\">−{d.Points}</td></tr>");
            h.Append("</table>");
        }
        if (m.Health.NotTested.Count > 0)
            h.Append($"<p class=\"note\"><strong>Not tested:</strong> {E(string.Join(", ", m.Health.NotTested))}.</p>");
        if (m.Health.Skipped.Count > 0)
            h.Append($"<p class=\"note\"><strong>Skipped:</strong> {E(string.Join(", ", m.Health.Skipped))}.</p>");
        h.Append("</section>");

        // Test results.
        if (m.Tests.Count > 0)
        {
            h.Append("<section><h2>Test results</h2><table class=\"list\">");
            foreach (var t in m.Tests)
                h.Append($"<tr><td class=\"c\">{Chip(t.Outcome)}</td><td class=\"area\">{E(t.Title)}</td><td>{E(t.Detail ?? "")}</td></tr>");
            h.Append("</table></section>");
        }

        if (m.Stress is { } st) h.Append(StressSection(st));

        // Windows 11.
        h.Append($"<section><h2>Windows 11 {Chip(m.Windows11.Verdict switch { "Ready" => "Pass", "Not supported" => "Fail", _ => "Warn" }, m.Windows11.Verdict)}</h2>");
        if (m.Windows11.RunningWindows11) h.Append("<p class=\"note\">Already running Windows 11.</p>");
        h.Append("<table class=\"list\">");
        foreach (var c in m.Windows11.Checks)
            h.Append($"<tr><td class=\"c\">{Chip(c.State)}</td><td class=\"area\">{E(c.Title)}</td><td>{E(c.Detail)}" +
                     $"{(c.Hint is { } hint ? $"<div class=\"hint\">{E(hint)}</div>" : "")}</td></tr>");
        h.Append("</table></section>");

        // Specifications.
        h.Append("<section><h2>Specifications</h2><div class=\"grid\">");
        foreach (var s in m.Specs) h.Append(Table(s));
        h.Append("</div></section>");

        foreach (var s in m.WindowsAndSecurity)
            h.Append($"<section><h2>{E(s.Title)}</h2>{Table(s, heading: false)}</section>");

        h.Append($"""
            <footer>Created with {E(m.App)} on {m.Created.ToString("d MMM yyyy, HH:mm", CultureInfo.GetCultureInfo("en-GB"))}.
            The score covers the tests run in this session; Windows 11 readiness is reported separately.</footer>
            </main></body></html>
            """);
        return h.ToString();
    }

    private static string StressSection(ReportStress st)
    {
        var sb = new StringBuilder("<section><h2>Stress test</h2>");
        sb.Append($"<p>{E(Join($"{st.Minutes.ToString("0.#", Inv)} minutes under full load",
            st.MaxTempC is { } mx ? $"{mx:0} °C max{(st.TempApproximate ? " (approximate sensor)" : "")}" : null,
            st.AverageTempC is { } av ? $"{av:0} °C average" : null,
            st.SustainedClockPercent is { } p ? $"{p:0}% of base clock sustained" : null,
            st.Throttling))}.</p>");
        sb.Append(Chart(st.Series));
        return sb.Append("</section>").ToString();
    }

    /// <summary>Temperature (red, fixed 30–105 °C scale) and clock (blue, scaled to its peak) over the run.</summary>
    private static string Chart(IReadOnlyList<ReportSample> series)
    {
        if (series.Count < 2) return "";
        const double w = 800, hgt = 170, left = 36, bottom = 22;
        double maxT = Math.Max(series[^1].Second, 1);
        double maxClock = series.Max(s => s.ClockMHz ?? 0);
        string X(int sec) => (left + sec / maxT * (w - left - 8)).ToString("0.#", Inv);
        string YTemp(double c) => (hgt - bottom - (Math.Clamp(c, 30, 105) - 30) / 75 * (hgt - bottom - 8)).ToString("0.#", Inv);
        string YClock(double mhz) => (hgt - bottom - mhz / Math.Max(maxClock, 1) * (hgt - bottom - 8)).ToString("0.#", Inv);

        var sb = new StringBuilder($"<svg class=\"chart\" viewBox=\"0 0 {w} {hgt}\" role=\"img\" aria-label=\"Temperature and clock speed during the stress test\">");
        foreach (var c in new[] { 40, 60, 80, 100 })
            sb.Append($"<line x1=\"{left}\" x2=\"{w - 8}\" y1=\"{YTemp(c)}\" y2=\"{YTemp(c)}\" class=\"grid\"/><text x=\"0\" y=\"{YTemp(c)}\" dy=\"4\">{c}°</text>");
        sb.Append($"<text x=\"{left}\" y=\"{hgt - 4}\">0</text><text x=\"{w - 8}\" y=\"{hgt - 4}\" text-anchor=\"end\">{maxT / 60:0.#} min</text>");
        if (series.Any(s => s.ClockMHz is not null))
            sb.Append($"<polyline class=\"clock\" points=\"{string.Join(' ', series.Where(s => s.ClockMHz is not null).Select(s => $"{X(s.Second)},{YClock(s.ClockMHz!.Value)}"))}\"/>");
        if (series.Any(s => s.TempC is not null))
            sb.Append($"<polyline class=\"temp\" points=\"{string.Join(' ', series.Where(s => s.TempC is not null).Select(s => $"{X(s.Second)},{YTemp(s.TempC!.Value)}"))}\"/>");
        sb.Append("</svg><div class=\"legend\"><span class=\"k temp\"></span>CPU temperature <span class=\"k clock\"></span>CPU clock");
        if (maxClock > 0) sb.Append($" (peak {maxClock / 1000:0.00} GHz)");
        return sb.Append("</div>").ToString();
    }

    private static string Table(ReportSection s, bool heading = true)
    {
        var sb = new StringBuilder("<div class=\"spec\">");
        if (heading) sb.Append($"<h3>{E(s.Title)}</h3>");
        sb.Append("<table>");
        foreach (var r in s.Rows)
            sb.Append($"<tr><th>{E(r.Label)}</th><td>{E(r.Value)}{(r.Status is { } st && st != "Pass" ? $" {Chip(st)}" : "")}</td></tr>");
        return sb.Append("</table></div>").ToString();
    }

    private static string Chip(string state, string? label = null)
    {
        string cls = state switch { "Pass" => "pass", "Warn" or "Minor" => "warn", "Fail" => "fail", _ => "neutral" };
        string text = label ?? state switch { "Pass" => "Passed", "Warn" => "Warning", "Fail" => "Failed", "Skipped" => "Skipped", _ => state };
        return $"<span class=\"chip {cls}\">{E(text)}</span>";
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string E(string s) => WebUtility.HtmlEncode(s);

    // Colours: Windows 11 light-theme status colours; every text/background pair is at least 4.5:1.
    private const string Css = """
        :root{--fg:#1a1a1a;--muted:#5d5d5d;--line:#e5e5e5;--accent:#005fb8;
        --pass:#0f7b0f;--pass-bg:#dff6dd;--warn:#8a5300;--warn-bg:#fff4ce;--fail:#c42b1c;--fail-bg:#fde7e9;--neutral:#5d5d5d;--neutral-bg:#f0f0f0}
        *{box-sizing:border-box}
        body{margin:0;background:#f3f3f3;color:var(--fg);font:14px/1.45 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif}
        main{max-width:920px;margin:24px auto;background:#fff;padding:40px 48px;border-radius:12px;box-shadow:0 2px 12px rgba(0,0,0,.08)}
        header{display:flex;justify-content:space-between;gap:24px;align-items:flex-start;padding-bottom:24px;border-bottom:1px solid var(--line)}
        .eyebrow{font-size:12px;letter-spacing:.04em;text-transform:uppercase;color:var(--muted)}
        h1{font:600 30px/1.2 "Segoe UI Variable Display","Segoe UI",sans-serif;margin:4px 0 6px}
        .meta{color:var(--muted)}
        .score{min-width:132px;text-align:center;border-radius:12px;padding:12px 16px;border:2px solid}
        .score.pass{border-color:var(--pass)}.score.warn{border-color:var(--warn)}.score.fail{border-color:var(--fail)}
        .score .num{font:600 44px/1 "Segoe UI Variable Display","Segoe UI",sans-serif}.score .of{color:var(--muted);font-size:12px;margin-bottom:8px}
        h2{font:600 18px/1.3 "Segoe UI Variable Display","Segoe UI",sans-serif;margin:32px 0 10px;display:flex;align-items:center;gap:10px}
        h3{font-size:14px;margin:0 0 4px}
        table{width:100%;border-collapse:collapse}
        td,th{padding:7px 8px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}
        th{font-weight:400;color:var(--muted);width:42%}
        .list td.c{width:92px}.list td.area{width:190px;font-weight:600}.list td.pts{width:40px;text-align:right;color:var(--muted)}
        .chip{display:inline-block;padding:1px 10px;border-radius:11px;font-size:12px;font-weight:600;white-space:nowrap}
        .chip.pass{color:var(--pass);background:var(--pass-bg)}.chip.warn{color:var(--warn);background:var(--warn-bg)}
        .chip.fail{color:var(--fail);background:var(--fail-bg)}.chip.neutral{color:var(--neutral);background:var(--neutral-bg)}
        .hint{margin-top:4px;padding:6px 10px;background:#f5f5f5;border-radius:4px}
        .note{color:var(--muted)}
        .grid{display:grid;grid-template-columns:1fr 1fr;gap:20px 32px}
        .chart{width:100%;height:auto;margin-top:4px}.chart text{font-size:11px;fill:var(--muted)}
        .chart .grid{stroke:var(--line)}.chart polyline{fill:none;stroke-width:2}
        .chart .temp{stroke:var(--fail)}.chart .clock{stroke:var(--accent);opacity:.8}
        .legend{color:var(--muted);font-size:12px}.legend .k{display:inline-block;width:14px;height:3px;margin:0 6px 3px 12px;vertical-align:middle}
        .legend .k:first-child{margin-left:0}.legend .temp{background:var(--fail)}.legend .clock{background:var(--accent)}
        footer{margin-top:36px;padding-top:12px;border-top:1px solid var(--line);color:var(--muted);font-size:12px}
        @media (max-width:700px){main{padding:24px}.grid{grid-template-columns:1fr}header{flex-direction:column}}
        @media print{body{background:#fff}main{box-shadow:none;margin:0;padding:0;max-width:none}section,tr,.spec{break-inside:avoid}}
        """;
}
