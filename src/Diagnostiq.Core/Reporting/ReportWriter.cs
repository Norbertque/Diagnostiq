using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diagnostiq.Core.Reporting;

public sealed record SavedReport(string HtmlPath, string JsonPath);

/// <summary>
/// Saves the report as HTML (to read and print) and JSON (for records or tools), in a Reports
/// folder next to the exe so a USB stick collects every laptop's report; falls back to
/// Documents when that folder isn't writable.
/// </summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SavedReport Save(ReportModel model, string? folder = null)
    {
        folder ??= DefaultFolder();
        Directory.CreateDirectory(folder);
        string name = FileName(model);
        var html = Path.Combine(folder, name + ".html");
        var json = Path.Combine(folder, name + ".json");
        File.WriteAllText(html, HtmlReport.Render(model));
        File.WriteAllText(json, JsonSerializer.Serialize(model, Json));
        return new SavedReport(html, json);
    }

    public static string DefaultFolder()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "Reports");
        if (IsWritable(beside)) return beside;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Diagnostiq Reports");
    }

    private static bool IsWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>"2026-09-30 1412 Dell Latitude 7430 52C2YT3": sorts by date, readable in Explorer.</summary>
    internal static string FileName(ReportModel m)
    {
        var raw = $"{m.Created:yyyy-MM-dd HHmm} {m.Device.Name}{(m.Device.Serial is { } s ? " " + s : "")}";
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(raw.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim(' ', '.');
        return clean.Length > 120 ? clean[..120] : clean;
    }
}
