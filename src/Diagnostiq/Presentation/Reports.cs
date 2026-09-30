using System.Diagnostics;
using System.IO;
using Diagnostiq.Core;
using Diagnostiq.Core.Reporting;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.Presentation;

/// <summary>Saving and opening reports from the UI.</summary>
public static class Reports
{
    /// <summary>Combines the session with a run that hasn't been merged yet (the Automatic summary).</summary>
    public static TestRun Combine(TestRun session, TestRun? current)
    {
        var all = new TestRun();
        all.MergeFrom(session);
        if (current is not null && !ReferenceEquals(current, session)) all.MergeFrom(current);
        return all;
    }

    public static Task<SavedReport> SaveAsync(SystemSnapshot snapshot, TestRun run) =>
        Task.Run(() => ReportWriter.Save(ReportModel.Build(snapshot, run)));

    /// <summary>
    /// Opens through Explorer so the browser starts with normal rights, not the app's
    /// administrator rights (browsers warn about, or refuse, running elevated).
    /// </summary>
    public static void Open(string path) => Explorer($"\"{path}\"");

    public static void ShowInFolder(string path) => Explorer($"/select,\"{path}\"");

    private static void Explorer(string args)
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var _ = Process.Start(new ProcessStartInfo(explorer, args) { UseShellExecute = false });
    }
}
