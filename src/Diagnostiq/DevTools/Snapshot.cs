#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Diagnostiq.DevTools;

/// <summary>
/// Debug-only: <c>Diagnostiq.exe --snapshot out.png [--theme light|dark] [--size 1280x800]</c>
/// renders the main window to a PNG and exits, so layouts can be checked in both
/// themes and at several sizes without driving the desktop.
/// Mica can't be captured by RenderTargetBitmap, so snapshots use a solid backdrop.
/// </summary>
internal static class Snapshot
{
    public static bool TryParse(string[] args, out string path, out ApplicationTheme theme, out Size size)
    {
        path = ""; theme = ApplicationTheme.Light; size = new Size(1280, 800);
        int i = Array.IndexOf(args, "--snapshot");
        if (i < 0 || i + 1 >= args.Length) return false;
        path = Path.GetFullPath(args[i + 1]);

        int t = Array.IndexOf(args, "--theme");
        if (t >= 0 && t + 1 < args.Length && args[t + 1].Equals("dark", StringComparison.OrdinalIgnoreCase))
            theme = ApplicationTheme.Dark;

        int s = Array.IndexOf(args, "--size");
        if (s >= 0 && s + 1 < args.Length)
        {
            var parts = args[s + 1].Split('x');
            if (parts.Length == 2 && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h))
                size = new Size(w, h);
        }
        return true;
    }

    public static void Capture(FluentWindow window, string path, ApplicationTheme theme, Size size)
    {
        window.WindowBackdropType = WindowBackdropType.None;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;  // render off-screen; nothing flashes on the desktop
        window.Width = size.Width;
        window.Height = size.Height;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: false);
        Theme.ThemeService.ApplyTokens(theme);

        window.ContentRendered += (_, _) =>
        {
            window.Dispatcher.InvokeAsync(() =>
            {
                var root = (FrameworkElement)window.Content;
                var bg = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
                var dpi = VisualTreeHelper.GetDpi(window);
                var bmp = new RenderTargetBitmap(
                    (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                // Render the element itself (a VisualBrush would stretch its overflow bounds
                // into the rect and distort the image); paint the solid backdrop first.
                var backdrop = new DrawingVisual();
                using (var dc = backdrop.RenderOpen())
                    dc.DrawRectangle(bg, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                bmp.Render(backdrop);
                bmp.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var fs = File.Create(path)) encoder.Save(fs);
                Application.Current.Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
    }
}
#endif
