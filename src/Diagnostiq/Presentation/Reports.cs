using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Diagnostiq.Core;
using Diagnostiq.Core.Reporting;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.Presentation;

/// <summary>Saving and opening reports from the UI.</summary>
public static partial class Reports
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
    /// "Download": the user picks where the report goes (Downloads, a USB stick…) and whether it's the
    /// page or the JSON data. Returns the saved path, or null when the dialog was cancelled.
    /// </summary>
    public static async Task<string?> DownloadAsync(Window owner, SystemSnapshot snapshot, TestRun run)
    {
        var model = await Task.Run(() => ReportModel.Build(snapshot, run));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Download report",
            FileName = ReportWriter.SuggestedName(model),
            DefaultExt = ".html",
            Filter = "Report page (*.html)|*.html|Report data (*.json)|*.json",
            InitialDirectory = DownloadsFolder(),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(owner) != true) return null;
        var path = dialog.FileName;
        await Task.Run(() => ReportWriter.SaveAs(model, path));
        return path;
    }

    private static string DownloadsFolder()
    {
        // FOLDERID_Downloads follows a moved or OneDrive-redirected Downloads folder.
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(id, 0, 0, out var ptr) == 0)
        {
            try { if (Marshal.PtrToStringUni(ptr) is { Length: > 0 } path && Directory.Exists(path)) return path; }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);

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
