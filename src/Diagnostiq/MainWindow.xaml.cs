using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Diagnostiq.Core;
using Diagnostiq.AutoRun;
using Diagnostiq.AutoRun.Steps;
using Diagnostiq.Core.Sensors;
using Diagnostiq.Core.Stress;
using Diagnostiq.Core.Testing;
using Diagnostiq.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Diagnostiq;

public partial class MainWindow : FluentWindow
{
    private readonly SnackbarService _snackbar = new();
    private HomeView? _home;
    private ManualView? _manual;
    private bool _closeConfirmed;
    private bool _closePending;

    public MainWindow()
    {
        InitializeComponent();
        _snackbar.SetSnackbarPresenter(SnackbarHost);
        Closing += OnClosing;
        SourceInitialized += (_, _) => FitToWorkArea();
    }

    /// <summary>
    /// Small screens (1366×768, or 1080p at 150 %) are shorter than the default size: never open larger
    /// than the work area of the monitor the window opens on, where the title bar could end up off-screen,
    /// and start maximised when it's short. Measured once the window has a handle, so it's that monitor
    /// at its own scale, not the primary one.
    /// </summary>
    private void FitToWorkArea()
    {
        if (WindowStartupLocation != WindowStartupLocation.CenterScreen) return;   // placed by the caller (dev snapshots)
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (Interop.MonitorWorkArea.Of(hwnd) is not { } px) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var area = new Rect(px.X / dpi.DpiScaleX, px.Y / dpi.DpiScaleY, px.Width / dpi.DpiScaleX, px.Height / dpi.DpiScaleY);

        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width - 16);
        Height = Math.Min(Height, area.Height - 16);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
        if (area.Height < 800) WindowState = WindowState.Maximized;
    }

    public SystemSnapshot? Snapshot { get; private set; }

    /// <summary>Live sensors, opened during startup; replaced if the driver is installed later.</summary>
    public SensorService? Sensors { get; set; }

    /// <summary>Off for dev snapshots so captures don't catch a view mid-fade.</summary>
    public bool AnimationsEnabled { get; set; } = SystemParameters.ClientAreaAnimation;

    /// <summary>Raised after a view is shown; the name is the view's class name.</summary>
    public event Action<string>? ViewShown;

    /// <summary>Shows the loading screen and reads the hardware in the background.</summary>
    public async void Start()
    {
        var loading = new LoadingView();
        Navigate(loading);
        Snapshot = await SnapshotBuilder.BuildAsync(loading.Progress);
        Sensors = Snapshot.Sensors.Value;
        await loading.FinishAsync(Snapshot);
        ShowHome();
    }

    public void ShowHome() => Navigate(_home ??= new HomeView(this, Snapshot!));

    public void ShowManual(string? page = null)
    {
        _manual ??= new ManualView(this, Snapshot!);
        if (page is not null) _manual.Select(page);
        Navigate(_manual);
    }

    /// <summary>Every result from this session (Automatic runs and single tests), for the report.</summary>
    public TestRun Session { get; } = new();

    /// <summary>Raised on the UI thread after a test window closes and the session changed.</summary>
    public event Action? SessionChanged;

    /// <summary>The last Automatic run, kept for its summary.</summary>
    public TestRun? LastRun { get; private set; }

    /// <summary>Manual mode: one step in the same fullscreen shell, recording straight into the session.</summary>
    public void RunSingle(StepView step, StressPreset preset = StressPreset.Standard)
    {
        var context = new AutoRunContext(Snapshot!, Sensors, preset, Session);
        if (!step.IsApplicable(context))
        {
            Notify("Not available", $"{step.Title} doesn't apply to this laptop.");
            return;
        }
        var window = new AutoRunWindow(context, [step], single: true) { Owner = this };
        IsEnabled = false;
        window.Closed += (_, _) =>
        {
            IsEnabled = true;
            Activate();
            SessionChanged?.Invoke();
        };
        window.Show();
    }

    /// <summary>The Automatic run's steps, in order.</summary>
    public static List<StepView> AutomaticSteps() =>
    [
        new ChecksStep(),
        new StressStep(),
        new DiskSpeedStep(),
        new DisplayStep(),
        new BrightnessStep(),
        new KeyboardStep(),
        new TouchpadStep(),
        new SpeakersStep(),
        new HeadphonesStep(),
        new MicrophoneStep(),
        new WebcamStep(),
        new UsbStep(),
        new ChargerStep(),
    ];

    /// <param name="steps">Defaults to the full run; the dev snapshot tool passes a subset.</param>
    /// <param name="configure">Lets the dev snapshot tool render the window off-screen.</param>
    public AutoRunWindow StartAutomatic(StressPreset preset, IEnumerable<StepView>? steps = null, Action<AutoRunWindow>? configure = null)
    {
        var context = new AutoRunContext(Snapshot!, Sensors, preset, new TestRun()) { Session = Session };
        var run = new AutoRunWindow(context, steps ?? AutomaticSteps()) { Owner = this };
        configure?.Invoke(run);
        IsEnabled = false;
        run.Closed += (_, _) =>
        {
            IsEnabled = true;
            LastRun = run.Run;
            Session.MergeFrom(run.Run);
            Activate();
            SessionChanged?.Invoke();
        };
        run.Show();
        return run;
    }

    public void ShowWin11() => Navigate(new Win11View(this, Snapshot!));

    public void Navigate(FrameworkElement view)
    {
        Host.Content = view;
        UiWatchdog.Where = view.GetType().Name;
        if (AnimationsEnabled)
        {
            // Fluent "entrance": short fade + 12 px rise, decelerating.
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var shift = new TranslateTransform(0, 12);
            view.RenderTransform = shift;
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        }
        view.Loaded += Shown;
        void Shown(object? sender, RoutedEventArgs e)
        {
            view.Loaded -= Shown;
            // The button that led here has left the tree; start keyboard focus at the top of the new view.
            view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            ViewShown?.Invoke(view.GetType().Name);
        }
    }

    /// <param name="timeout">How long the message stays; 4 s by default, longer for errors worth reading.</param>
    public void Notify(string title, string message, ControlAppearance appearance = ControlAppearance.Secondary,
        SymbolRegular icon = SymbolRegular.Info24, TimeSpan? timeout = null)
    {
        _snackbar.Show(title, message, appearance, new SymbolIcon(icon), timeout ?? TimeSpan.FromSeconds(4));
        // The snackbar raises no UI Automation events of its own, so screen readers would miss it.
        UIElementAutomationPeer.CreatePeerForElement(this)?.RaiseNotificationEvent(AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent, $"{title}. {message}", "Diagnostiq.Notify");
    }

    public Task<ContentDialogResult> AskAsync(string title, string message, string primary, string close = "Cancel") =>
        new ContentDialog(DialogHost)
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Primary,
        }.ShowAsync();

    /// <summary>If this session installed the sensor driver, offer to remove it before closing.</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || !PawnIoSetup.InstalledByUs) { Cleanup(); return; }
        e.Cancel = true;
        if (_closePending) return;   // already asking, or the driver is being removed
        _closePending = true;
        try
        {
            var answer = await AskAsync("Remove the sensor driver?",
                "Diagnostiq installed the PawnIO sensor driver for this session. Remove it now, or keep it if you'll test this laptop again.",
                "Remove", "Keep it");
            if (answer == ContentDialogResult.Primary)
            {
                // The setup can take a minute and shows nothing; keep the window from taking clicks meanwhile.
                IsEnabled = false;
                Notify("Removing the sensor driver", "Diagnostiq closes when it's done.", timeout: TimeSpan.FromMinutes(5));
                Sensors?.Dispose();   // release the driver handle before uninstalling
                PawnIoSetupResult result;
                try { result = await Task.Run(PawnIoSetup.UninstallAsync); }
                catch (Exception ex)
                {
                    // The uninstaller is a separate program; whatever it does, the app must still close.
                    App.LogError(ex);
                    result = new(false, $"Couldn't remove the PawnIO sensor driver: {ex.Message}");
                }
                if (!result.Success)
                    System.Windows.MessageBox.Show(result.Message, "Diagnostiq", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _closePending = false;
        }
    }

    private void Cleanup()
    {
        try { Sensors?.Dispose(); } catch (ObjectDisposedException) { }
    }
}
