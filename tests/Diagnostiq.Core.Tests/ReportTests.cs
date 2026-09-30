using System.Text.Json;
using Diagnostiq.Core.Reporting;
using Diagnostiq.Core.Scoring;

namespace Diagnostiq.Core.Tests;

public class ReportTests
{
    private static ReportModel Sample(string name = "Dell Latitude 7430", string? serial = "52C2YT3") => new(
        "Diagnostiq 2.0.0",
        new DateTimeOffset(2026, 9, 30, 14, 12, 0, TimeSpan.FromHours(2)),
        new ReportDevice(name, "Dell", "Latitude 7430", null, "Service Tag", serial, "1.41.0", "26 May 2026"),
        new ReportHealth(70, "Needs repair", [new Deduction("Keyboard", "82 of 84 keys worked. Not working: F7, <Right Shift>.", Severity.Critical, 30)],
            ["Camera"], ["USB ports"]),
        new ReportWin11("Ready", true, [new ReportWin11Check("TPM 2.0", "Pass", "TPM 2.0.", null)]),
        [new ReportTest("keyboard", "Keyboard", "Fail", "82 of 84 keys worked.")],
        new ReportStress(5, 91, 84, false, 118, null,
            Enumerable.Range(0, 30).Select(i => new ReportSample(i * 10, 70 + i % 5, 3000 + i, 100)).ToList()),
        [new ReportSection("Processor", [new ReportRow("Name", "12th Gen Intel Core i7-1265U")])],
        [new ReportSection("Windows & security", [new ReportRow("Activation", "Activated (OEM)", "Pass")])]);

    [Fact]
    public void Html_contains_the_essentials_and_escapes_text()
    {
        var html = HtmlReport.Render(Sample());
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("Dell Latitude 7430", html);
        Assert.Contains("Service Tag 52C2YT3", html);
        Assert.Contains(">70<", html);
        Assert.Contains("Needs repair", html);
        Assert.Contains("&lt;Right Shift&gt;", html);      // user-visible text is encoded
        Assert.DoesNotContain("<Right Shift>", html);
        Assert.Contains("<svg", html);                     // stress chart
        Assert.Contains("Not tested:", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void Json_round_trips()
    {
        var dir = Directory.CreateTempSubdirectory("diag-report").FullName;
        try
        {
            var saved = ReportWriter.Save(Sample(), dir);
            Assert.True(File.Exists(saved.HtmlPath));
            using var doc = JsonDocument.Parse(File.ReadAllText(saved.JsonPath));
            Assert.Equal(70, doc.RootElement.GetProperty("health").GetProperty("score").GetInt32());
            Assert.Equal("Critical", doc.RootElement.GetProperty("health").GetProperty("deductions")[0].GetProperty("severity").GetString());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("Dell Latitude 7430", "52C2YT3", "2026-09-30 1412 Dell Latitude 7430 52C2YT3")]
    [InlineData("HP EliteBook 840 G5/G6", null, "2026-09-30 1412 HP EliteBook 840 G5-G6")]
    public void File_names_are_safe_and_sortable(string name, string? serial, string expected) =>
        Assert.Equal(expected, ReportWriter.FileName(Sample(name, serial)));
}
