using System.Windows;

namespace Diagnostiq;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;

#if DEBUG
        if (DevTools.Snapshot.TryParse(e.Args, out var path, out var theme, out var size))
        {
            DevTools.Snapshot.Capture(window, path, theme, size);
            window.Show();
            return;
        }
#endif

        Theme.ThemeService.Attach(window);
        window.Show();
    }
}
