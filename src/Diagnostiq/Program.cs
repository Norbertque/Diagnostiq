using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace Diagnostiq;

/// <summary>
/// Custom entry point so the native splash matches the Windows light/dark setting and the
/// display scale. The splash is drawn by WPF before any app code, XAML or hardware probes
/// run, so even an old laptop shows something within a second of double-clicking.
/// </summary>
public static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool devTool = args.Contains("--snapshot") || args.Contains("--render-assets");
        if (!devTool)
            new SplashScreen($"Assets/splash-{(UsesDarkTheme() ? "dark" : "light")}-{SplashScale()}.png").Show(autoClose: true, topMost: false);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    internal static bool UsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (System.Security.SecurityException) { return false; }
    }

    /// <summary>The splash is a bitmap shown at pixel size, so pick the closest pre-rendered scale.</summary>
    private static int SplashScale()
    {
        uint dpi;
        try { dpi = GetDpiForSystem(); } catch (EntryPointNotFoundException) { dpi = 96; }
        int percent = (int)(dpi * 100 / 96);
        return percent < 125 ? 100 : percent < 175 ? 150 : 200;
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
