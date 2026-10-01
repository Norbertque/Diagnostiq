using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Diagnostiq;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        Controls.SmoothScrolling.Register();

#if DEBUG
        if (DevTools.AssetRenderer.TryParse(e.Args, out var assetDir))
        {
            DevTools.AssetRenderer.Render(assetDir);
            Shutdown();
            return;
        }
#endif

        var window = new MainWindow();
        MainWindow = window;

#if DEBUG
        if (DevTools.Snapshot.TryParse(e.Args, out var path, out var theme, out var size, out var view))
        {
            DevTools.Snapshot.Capture(window, path, theme, size, view);
            window.Show();
            window.Start();
            return;
        }
#endif

        Theme.ThemeService.Attach(window);
        window.Show();
        window.Start();
    }

    /// <summary>Where unexpected errors are written, for support.</summary>
    public static string ErrorLogPath => Path.Combine(Path.GetTempPath(), "Diagnostiq", "errors.log");

    /// <summary>Appends an unexpected error to <see cref="ErrorLogPath"/>; never throws.</summary>
    public static void LogError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
            File.AppendAllText(ErrorLogPath, $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Last resort: log, tell the user, keep running (a failed view shouldn't kill a 15-minute test).</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Interop.KeyboardHook.ReleaseAll();   // never leave the keyboard captured after an error
        LogError(e.Exception);

        System.Windows.MessageBox.Show($"Something went wrong: {e.Exception.Message}\n\nThe app will keep running. Details were saved to {ErrorLogPath}.",
            "Diagnostiq", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
