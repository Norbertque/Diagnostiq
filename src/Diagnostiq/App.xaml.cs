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

    /// <summary>Last resort: log, tell the user, keep running (a failed view shouldn't kill a 15-minute test).</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Interop.KeyboardHook.ReleaseAll();   // never leave the keyboard captured after an error
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Diagnostiq");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTime.Now:O}\n{e.Exception}\n\n");
        }
        catch (IOException) { }

        System.Windows.MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nThe app will keep running.",
            "Diagnostiq", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
