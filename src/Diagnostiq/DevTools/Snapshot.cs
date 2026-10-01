#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Diagnostiq.DevTools;

/// <summary>
/// Debug-only: <c>Diagnostiq.exe --snapshot out.png [--theme light|dark] [--size 1280x800] [--view loading|home|win11|auto|auto-summary|manual-PAGE] [--full] [--session]</c>
/// renders a view of the main window to a PNG and exits, so layouts can be checked in both
/// themes and at several sizes without driving the desktop. The hardware probes run for real.
/// Mica can't be captured by RenderTargetBitmap, so snapshots use a solid backdrop.
/// </summary>
internal static class Snapshot
{
    public static bool TryParse(string[] args, out string path, out ApplicationTheme theme, out Size size, out string view)
    {
        path = ""; theme = ApplicationTheme.Light; size = new Size(1280, 800); view = "home";
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

        int v = Array.IndexOf(args, "--view");
        if (v >= 0 && v + 1 < args.Length) view = args[v + 1].ToLowerInvariant();
        _fullPage = args.Contains("--full");
        _seedSession = args.Contains("--session");
        _liveDevices = args.Contains("--live");
        int f = Array.IndexOf(args, "--from");
        if (f >= 0 && f + 1 < args.Length) _fromStep = args[f + 1];
        int d = Array.IndexOf(args, "--delay");
        if (d >= 0 && d + 1 < args.Length && double.TryParse(args[d + 1], out var secs)) _delay = TimeSpan.FromSeconds(secs);
        return true;
    }

    // --full: render the whole scrollable page, not just what fits on screen (windows can't exceed the screen height).
    private static bool _fullPage;

    // --session: pre-fill the session with sample results (Home "last check" card, Tests and Report pages).
    private static bool _seedSession;

    // --live (skipspam only): run the steps with the real keyboard hook, sound, microphone and camera.
    private static bool _liveDevices;

    // --view auto: --from StepClassName starts the run at that step; --delay seconds before capturing.
    private static string? _fromStep;
    private static TimeSpan _delay = TimeSpan.FromSeconds(3);

    public static void Capture(MainWindow window, string path, ApplicationTheme theme, Size size, string view)
    {
        window.AnimationsEnabled = false;
        window.WindowBackdropType = WindowBackdropType.None;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;   // MainWindow maximises itself on short screens
        window.Left = -10000;  // render off-screen; nothing flashes on the desktop
        window.MinWidth = Math.Min(window.MinWidth, size.Width);
        window.MinHeight = Math.Min(window.MinHeight, size.Height);
        window.Width = size.Width;
        window.Height = size.Height;
        // Same accent colours as the real app (SystemThemeWatcher updates them), so contrast checks on snapshots hold.
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: true);
        Theme.ThemeService.ApplyTokens(theme);
        if (_seedSession)
            foreach (var r in SampleResults) window.Session.Record(r);

        window.ViewShown += name =>
        {
            switch (view, name)
            {
                case ("loading", "LoadingView"):
                    // Catch it mid-scan, with some steps ticked and some still running.
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                    timer.Tick += (_, _) => { timer.Stop(); Save(window, window.Host, path); };
                    timer.Start();
                    break;
                case ("home", "HomeView"):
                    Save(window, window.Host, path);
                    break;
                case ("report", "HomeView"):
                    // The HTML/JSON report for this machine and the session, written next to the path given.
                    var saved = Core.Reporting.ReportWriter.Save(Core.Reporting.ReportModel.Build(window.Snapshot!, window.Session), Path.GetDirectoryName(path));
                    Console.WriteLine(saved.HtmlPath);
                    Application.Current.Shutdown();
                    break;
                case ("win11", "HomeView"):
                    window.ShowWin11();
                    break;
                case (_, "HomeView") when view.StartsWith("manual-"):
                    window.ShowManual(view["manual-".Length..]);
                    break;
                case (_, "ManualView") when view.StartsWith("manual-"):
                    Save(window, window.Host, path);
                    break;
                case ("win11", "Win11View"):
                    Save(window, window.Host, path);
                    break;
                case ("auto" or "auto-summary", "HomeView"):
                    List<AutoRun.StepView> steps = view == "auto-summary"
                        ? [new SampleResultsStep()]
                        : MainWindow.AutomaticSteps().SkipWhile(s => _fromStep is not null && s.GetType().Name != _fromStep).ToList();
                    var run = window.StartAutomatic(Core.Stress.StressPreset.Quick, steps, w => OffScreen(w, size));
                    var wait = new DispatcherTimer { Interval = view == "auto-summary" ? TimeSpan.FromSeconds(1.5) : _delay };
                    wait.Tick += (_, _) => { wait.Stop(); Save(run, run.Stage, path); };
                    wait.Start();
                    break;
                case ("skipspam", "HomeView"):
                    SkipSpam(window, size);
                    break;
            }
        };
    }

    /// <summary>Normal, not maximized or topmost, and off-screen: nothing covers the desktop.</summary>
    private static void OffScreen(AutoRun.AutoRunWindow w, Size size)
    {
        w.WindowState = WindowState.Normal;
        w.Topmost = false;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -10000;
        w.Top = 0;
        w.Width = size.Width;
        w.Height = size.Height;
        w.ShowActivated = false;
        w.AnimationsEnabled = false;
        w.Context.LiveDevices = _liveDevices;   // normally off: no keyboard hook, sound, microphone or camera while capturing
    }

    /// <summary>
    /// <c>--view skipspam [--delay 0.1]</c>: runs the whole Automatic check and clicks Skip every interval,
    /// like a user hammering the button. Prints OK when the summary appears, STUCK if the run stops
    /// moving, HANG (and exits) if the UI thread stops responding for 5 s.
    /// </summary>
    private static void SkipSpam(MainWindow window, Size size)
    {
        // --from HangStep: start with a step stuck in a call that ignores Skip, as a hung driver would be.
        List<AutoRun.StepView>? steps = _fromStep == nameof(HangStep) ? [new HangStep(), .. MainWindow.AutomaticSteps()] : null;
        var run = window.StartAutomatic(Core.Stress.StressPreset.Quick, steps, w => OffScreen(w, size));
        var started = DateTime.UtcNow;
        long lastTick = Environment.TickCount64;
        int clicks = 0;
        string current = "";
        var clicker = new DispatcherTimer { Interval = _delay };
        clicker.Tick += (_, _) =>
        {
            Volatile.Write(ref lastTick, Environment.TickCount64);
            if (run.StepTitle.Text != current) Console.WriteLine($"{(DateTime.UtcNow - started).TotalSeconds,5:0.0}s  {run.StepCounter.Text}: {current = run.StepTitle.Text}");
            if (run.Stage.Content is AutoRun.SummaryView)
            {
                clicker.Stop();
                Console.WriteLine($"OK: summary after {clicks} skips in {(DateTime.UtcNow - started).TotalSeconds:0.0} s");
                Application.Current.Shutdown();
                return;
            }
            if (run.SkipButton.IsVisible && run.SkipButton.IsEnabled)
            {
                run.SkipButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                clicks++;
            }
            if ((DateTime.UtcNow - started).TotalSeconds > 120)
            {
                Console.WriteLine($"STUCK: UI responsive but still on '{current}' after {clicks} skips");
                Application.Current.Shutdown();
            }
        };
        clicker.Start();
        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(500);
                if (Environment.TickCount64 - Volatile.Read(ref lastTick) < 5000) continue;
                Console.WriteLine($"HANG: UI thread blocked for 5 s on '{current}' after {clicks} skips");
                Console.Out.Flush();
                Environment.Exit(2);
            }
        }) { IsBackground = true }.Start();
    }

    /// <summary>The page's main scroll area: the first ScrollViewer in the visual tree.</summary>
    private static System.Windows.Controls.ScrollViewer? FindScroller(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.ScrollViewer sv && sv.TemplatedParent is not System.Windows.Controls.ItemsControl) return sv;
            if (FindScroller(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>A typical mix of outcomes, so result layouts can be checked without a 15-minute run.</summary>
    private static readonly Core.Testing.TestResult[] SampleResults =
    [
        new("network", "Wi-Fi and internet", Core.Testing.TestOutcome.Pass, "Online through Home, 14 ms. The Wi-Fi adapter sees 6 networks."),
        new("cpu", "Processor under load", Core.Testing.TestOutcome.Pass, "Stable, 91 °C max. Held 118% of its base speed."),
        new("memory", "Memory", Core.Testing.TestOutcome.Pass, "9.8 GB tested over 3 passes, no errors."),
        new("surface", "Disk surface scan", Core.Testing.TestOutcome.Skipped, "Needs administrator rights."),
        new("diskspeed", "Disk speed", Core.Testing.TestOutcome.Pass, "Read 3,120 MB/s, write 1,870 MB/s, 21,400 random reads per second."),
        new("keyboard", "Keyboard", Core.Testing.TestOutcome.Fail, "82 of 84 keys worked. Not working: F7, Right Shift."),
        new("touchpad", "Touchpad", Core.Testing.TestOutcome.Warn, "96% of the surface, both buttons and vertical scroll work; horizontal scroll didn't register."),
    ];

    /// <summary>A step stuck in a call that never returns and ignores cancellation, like a hung driver.</summary>
    private sealed class HangStep : AutoRun.StepView
    {
        public override string Id => "hang";
        public override string Title => "Stuck step";
        protected override Task OnStartAsync(CancellationToken ct) => Task.Delay(Timeout.Infinite, CancellationToken.None);
    }

    /// <summary>Records <see cref="SampleResults"/> so the summary layout can be checked.</summary>
    private sealed class SampleResultsStep : AutoRun.StepView
    {
        public override string Id => "sample";
        public override string Title => "Sample results";
        public override AutoRun.StepMode Mode => AutoRun.StepMode.Automatic;

        protected override Task OnRunAsync(CancellationToken ct)
        {
            foreach (var r in SampleResults) Ctx.Run.Record(r);
            return Task.CompletedTask;
        }
    }

    private static void Save(Window window, System.Windows.Controls.ContentControl host, string path) =>
        window.Dispatcher.InvokeAsync(() =>
        {
            var root = (FrameworkElement)window.Content;
            var bg = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
            var dpi = VisualTreeHelper.GetDpi(window);
            RenderTargetBitmap bmp;

            var page = _fullPage && FindScroller(host) is { Content: FrameworkElement c } ? c : null;
            if (page is not null)
            {
                // Render the page content at its full height. RenderTargetBitmap draws a visual at its
                // offset inside the parent (margin + centering), and ancestors' clipping doesn't apply.
                var offset = VisualTreeHelper.GetOffset(page);
                double w = Math.Max(root.ActualWidth, offset.X + page.ActualWidth + page.Margin.Right);
                double h = offset.Y + page.ActualHeight + page.Margin.Bottom;
                var backdrop = new DrawingVisual();
                using (var dc = backdrop.RenderOpen())
                    dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
                bmp = new RenderTargetBitmap((int)(w * dpi.DpiScaleX), (int)(h * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bmp.Render(backdrop);
                bmp.Render(page);
            }
            else
            {
                bmp = new RenderTargetBitmap(
                    (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                // Render the element itself (a VisualBrush would stretch its overflow bounds
                // into the rect and distort the image); paint the solid backdrop first.
                var backdrop = new DrawingVisual();
                using (var dc = backdrop.RenderOpen())
                    dc.DrawRectangle(bg, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                bmp.Render(backdrop);
                bmp.Render(root);
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var fs = File.Create(path)) encoder.Save(fs);
            Application.Current.Shutdown();
        }, DispatcherPriority.ApplicationIdle);
}
#endif
