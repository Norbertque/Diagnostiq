using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Diagnostiq.Theme;

/// <summary>
/// Follows the Windows light/dark setting and patches the tokens Fluent gets wrong: its dark
/// "attention" text (#60CDFF) is 4.41:1 on its own background, below WCAG AA for the small
/// Info/Running pills, and its high-contrast themes leave every status colour at a #FF0000
/// placeholder, so pills would be red on red.
/// </summary>
public static class ThemeService
{
    private const string InfoForegroundKey = "Diag.InfoForegroundBrush";
    private static readonly Color InfoLight = Color.FromRgb(0x00, 0x5F, 0xB7); // 6.08:1
    private static readonly Color InfoDark = Color.FromRgb(0x99, 0xEB, 0xFF);  // 5.93:1

    // Status text and icons, and the pill and banner fills behind them (WPF-UI 4.3 HC dictionaries: all #FF0000).
    private static readonly string[] StatusForegroundKeys =
        ["SystemFillColorSuccessBrush", "SystemFillColorCautionBrush", "SystemFillColorCriticalBrush", "SystemFillColorNeutralBrush"];
    private static readonly string[] StatusBackgroundKeys =
        ["SystemFillColorSuccessBackgroundBrush", "SystemFillColorCautionBackgroundBrush", "SystemFillColorCriticalBackgroundBrush",
         "SystemFillColorAttentionBackgroundBrush", "SystemFillColorNeutralBackgroundBrush"];

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
        var resources = Application.Current.Resources;
        bool highContrast = theme == ApplicationTheme.HighContrast;

        // In high contrast, status shows as the user's own text-on-window colours; the icon shapes
        // still tell the states apart. App-level keys win over the theme dictionary, so removing
        // them hands the Fluent colours back when high contrast is switched off.
        foreach (var key in StatusForegroundKeys)
            if (highContrast) resources[key] = SystemColors.WindowTextBrush; else resources.Remove(key);
        foreach (var key in StatusBackgroundKeys)
            if (highContrast) resources[key] = SystemColors.WindowBrush; else resources.Remove(key);

        if (highContrast)
        {
            resources[InfoForegroundKey] = SystemColors.WindowTextBrush;
            return;
        }
        var brush = new SolidColorBrush(theme == ApplicationTheme.Dark ? InfoDark : InfoLight);
        brush.Freeze();
        resources[InfoForegroundKey] = brush;
    }
}
