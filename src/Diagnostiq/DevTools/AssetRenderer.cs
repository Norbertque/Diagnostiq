#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Diagnostiq.DevTools;

/// <summary>
/// Debug-only: <c>Diagnostiq.exe --render-assets &lt;dir&gt;</c> draws the app icon and the
/// splash screens from code, so they stay reproducible and match the design tokens.
/// Output: app.ico (16–256 px), icon.png and splash-{light|dark}-{100|150|200}.png.
/// </summary>
internal static class AssetRenderer
{
    public static readonly int[] SplashScales = [100, 150, 200];
    private const int SplashWidth = 520, SplashHeight = 320, ShadowMargin = 24;

    public static bool TryParse(string[] args, out string dir)
    {
        int i = Array.IndexOf(args, "--render-assets");
        dir = i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : "";
        return i >= 0 && dir.Length > 0;
    }

    public static void Render(string dir)
    {
        Directory.CreateDirectory(dir);
        WriteIco(Path.Combine(dir, "app.ico"), [16, 20, 24, 32, 40, 48, 64, 256]);
        SavePng(Icon(128), 128, 128, 2, Path.Combine(dir, "icon.png"));   // in-app logo, crisp up to 200 % scaling
        foreach (var dark in new[] { false, true })
            foreach (var scale in SplashScales)
                SavePng(Splash(dark), SplashWidth, SplashHeight, scale / 100.0,
                    Path.Combine(dir, $"splash-{(dark ? "dark" : "light")}-{scale}.png"));
    }

    /// <summary>Rounded accent square with a white stethoscope: "check-up for your laptop".</summary>
    public static FrameworkElement Icon(double size) => new Border
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.22),
        Background = new LinearGradientBrush(Color.FromRgb(0x2B, 0x8C, 0xE8), Color.FromRgb(0x00, 0x52, 0xA3), 45),
        Child = new SymbolIcon
        {
            Symbol = SymbolRegular.Stethoscope24,
            Filled = true,
            FontSize = size * 0.62,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static FrameworkElement Splash(bool dark)
    {
        var fg = dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);
        var secondary = dark ? Color.FromRgb(0xC5, 0xC5, 0xC5) : Color.FromRgb(0x5D, 0x5D, 0x5D);
        var display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        var text = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(Icon(72));
        content.Children.Add(new TextBlock
        {
            Text = "Diagnostiq", FontFamily = display, FontSize = 28, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 2),
        });
        content.Children.Add(new TextBlock
        {
            Text = "Laptop health check", FontFamily = text, FontSize = 14,
            Foreground = new SolidColorBrush(secondary), HorizontalAlignment = HorizontalAlignment.Center,
        });

        var status = new TextBlock
        {
            Text = "Starting…", FontFamily = text, FontSize = 12, Foreground = new SolidColorBrush(secondary),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20),
        };

        var card = new Border
        {
            Margin = new Thickness(ShadowMargin),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF9, 0xF9, 0xF9)),
            BorderBrush = new SolidColorBrush(dark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = dark ? 0.45 : 0.18 },
            Child = new Grid { Children = { content, status } },
        };
        return card;
    }

    private static BitmapSource Rasterize(FrameworkElement visual, double width, double height, double scale)
    {
        visual.Measure(new Size(width, height));
        visual.Arrange(new Rect(0, 0, width, height));
        visual.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static byte[] Png(BitmapSource bmp)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void SavePng(FrameworkElement visual, double width, double height, double scale, string path) =>
        File.WriteAllBytes(path, Png(Rasterize(visual, width, height, scale)));

    /// <summary>ICO with PNG-compressed entries (supported since Windows Vista).</summary>
    private static void WriteIco(string path, int[] sizes)
    {
        var images = sizes.Select(s => Png(Rasterize(Icon(s), s, s, 1))).ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
        int offset = 6 + 16 * images.Count;
        for (int i = 0; i < images.Count; i++)
        {
            byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(images[i].Length); w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) w.Write(img);
    }
}
#endif
