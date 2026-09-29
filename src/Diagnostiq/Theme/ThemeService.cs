using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Diagnostiq.Theme;

/// <summary>
/// Follows the Windows light/dark setting and patches the one token Fluent
/// gets wrong: its dark "attention" text (#60CDFF) is 4.41:1 on its own
/// background, below WCAG AA for the small Info/Running pills.
/// </summary>
public static class ThemeService
{
    private const string InfoForegroundKey = "Diag.InfoForegroundBrush";
    private static readonly Color InfoLight = Color.FromRgb(0x00, 0x5F, 0xB7); // 6.08:1
    private static readonly Color InfoDark = Color.FromRgb(0x99, 0xEB, 0xFF);  // 5.93:1

    private static bool _subscribed;

    /// <summary>Apply the system theme to <paramref name="window"/> and keep it in sync.</summary>
    public static void Attach(Window window)
    {
        if (!_subscribed)
        {
            ApplicationThemeManager.Changed += (theme, _) => ApplyTokens(theme);
            _subscribed = true;
        }

        SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, updateAccents: true);
        ApplyTokens(ApplicationThemeManager.GetAppTheme());
    }

    public static void ApplyTokens(ApplicationTheme theme)
    {
        var color = theme == ApplicationTheme.Dark ? InfoDark : InfoLight;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[InfoForegroundKey] = brush;
    }
}
