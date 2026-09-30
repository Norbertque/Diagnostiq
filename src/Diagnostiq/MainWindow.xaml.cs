using System.ComponentModel;
using System.Windows;
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
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        _snackbar.SetSnackbarPresenter(SnackbarHost);
        Closing += OnClosing;
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

    /// <summary>The last Automatic run, kept for Home and the report.</summary>
    public TestRun? LastRun { get; private set; }

    /// <summary>The Automatic run's steps, in order.</summary>
    public static List<StepView> AutomaticSteps() =>
    [
        new ChecksStep(),
        new StressStep(),
        new DiskSpeedStep(),
        new DisplayStep(),
        new BrightnessStep(),
    ];

    /// <param name="steps">Defaults to the full run; the dev snapshot tool passes a subset.</param>
    /// <param name="configure">Lets the dev snapshot tool render the window off-screen.</param>
    public AutoRunWindow StartAutomatic(StressPreset preset, IEnumerable<StepView>? steps = null, Action<AutoRunWindow>? configure = null)
    {
        var context = new AutoRunContext(Snapshot!, Sensors, preset, new TestRun());
        var run = new AutoRunWindow(context, steps ?? AutomaticSteps()) { Owner = this };
        configure?.Invoke(run);
        IsEnabled = false;
        run.Closed += (_, _) =>
        {
            IsEnabled = true;
            LastRun = run.Run;
            Activate();
        };
        run.Show();
        return run;
    }

    public void ShowWin11() => Navigate(new Win11View(this, Snapshot!));

    public void Navigate(FrameworkElement view)
    {
        Host.Content = view;
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
            ViewShown?.Invoke(view.GetType().Name);
        }
    }

    public void Notify(string title, string message, ControlAppearance appearance = ControlAppearance.Secondary, SymbolRegular icon = SymbolRegular.Info24) =>
        _snackbar.Show(title, message, appearance, new SymbolIcon(icon), TimeSpan.FromSeconds(4));

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
        var answer = await AskAsync("Remove the sensor driver?",
            "Diagnostiq installed the PawnIO sensor driver for this session. Remove it now, or keep it if you'll test this laptop again.",
            "Remove", "Keep it");
        if (answer == ContentDialogResult.Primary)
        {
            Sensors?.Dispose();   // release the driver handle before uninstalling
            var result = await Task.Run(PawnIoSetup.UninstallAsync);
            if (!result.Success)
                System.Windows.MessageBox.Show(result.Message, "Diagnostiq", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _closeConfirmed = true;
        Close();
    }

    private void Cleanup()
    {
        try { Sensors?.Dispose(); } catch (ObjectDisposedException) { }
    }
}
