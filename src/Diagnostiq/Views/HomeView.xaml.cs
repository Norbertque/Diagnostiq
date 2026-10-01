using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Diagnostiq.Core;
using Diagnostiq.Core.Formatting;
using Diagnostiq.Core.Scoring;
using Diagnostiq.Core.Sensors;
using Diagnostiq.Core.Stress;
using Diagnostiq.Core.Testing;
using Diagnostiq.Presentation;
using Wpf.Ui.Controls;

namespace Diagnostiq.Views;

public partial class HomeView : UserControl
{
    public static readonly DependencyProperty TileColumnsProperty =
        DependencyProperty.Register(nameof(TileColumns), typeof(int), typeof(HomeView), new PropertyMetadata(3));

    private readonly MainWindow _window;
    private readonly SystemSnapshot _snapshot;
    private readonly HomeModel _model;

    public HomeView(MainWindow window, SystemSnapshot snapshot)
    {
        InitializeComponent();
        _window = window;
        _snapshot = snapshot;
        _model = HomeModel.From(snapshot);
        DataContext = _model;

        FindingsList.ItemsSource = _model.Findings;
        FindingsCard.Visibility = _model.Findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TileList.ItemsSource = _model.Tiles;
        Win11Note.Visibility = _model.Win11.Note is null ? Visibility.Collapsed : Visibility.Visible;
        CopySerialButton.Visibility = _model.Serial is null ? Visibility.Collapsed : Visibility.Visible;
        AdminBanner.Visibility = snapshot.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        SetUpSensorBanner();

        // Named as on the Tests page and in the report. Non-breaking spaces/hyphen keep each name whole; the dot
        // stays with the item before it.
        string[] included = ["Processor under load", "Memory", "Disk surface scan", "Disk speed", "Screen", "Brightness", "Keyboard",
                             "Touchpad", "Speakers", "Headphone jack", "Microphone", "Camera", "USB ports", "Charger", "Battery under load",
                             "Wi-Fi and internet"];
        IncludedTests.Text = string.Join(" · ", included.Select(t => t.Replace(' ', ' ').Replace('-', '‑')));
        StandardRadio.IsChecked = true;
        SizeChanged += (_, e) =>
        {
            TileColumns = e.NewSize.Width >= 1060 ? 3 : e.NewSize.Width >= 700 ? 2 : 1;
            // Narrow: the Windows 11 status goes under the title instead of squeezing the serial number row.
            bool narrow = e.NewSize.Width < 1000;
            Grid.SetRow(Win11HeaderButton, narrow ? 1 : 0);
            Grid.SetColumn(Win11HeaderButton, narrow ? 0 : 1);
            Win11HeaderButton.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            Win11HeaderButton.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(16, 0, 0, 0);
        };
        Loaded += (_, _) => { _window.SessionChanged += UpdateLastCheck; UpdateLastCheck(); };
        Unloaded += (_, _) => _window.SessionChanged -= UpdateLastCheck;
    }

    /// <summary>Score of everything tested this session, so returning Home shows where things stand.</summary>
    private void UpdateLastCheck()
    {
        var results = _window.Session.Results;
        if (results.Count == 0) { LastCheckCard.Visibility = Visibility.Collapsed; return; }

        var health = HealthScore.Compute(_snapshot, _window.Session);
        LastCheckCard.Visibility = Visibility.Visible;
        if (health.NothingTested)
        {
            // Only skipped tests: a score of 100 here would read as a clean bill of health.
            LastCheckIcon.State = CheckState.Unknown;
            LastCheckTitle.Text = "No tests finished yet";
            LastCheckDetail.Text = "Everything run so far was skipped, so the score only reflects what the startup scan found.";
            return;
        }

        // Counted the way the Automatic summary counts them; the score itself also covers battery, drive and drivers.
        int tested = results.Count(r => r.Outcome != TestOutcome.Skipped);
        int failed = results.Count(r => r.Outcome == TestOutcome.Fail);
        int warned = results.Count(r => r.Outcome == TestOutcome.Warn);
        LastCheckIcon.State = health.Verdict switch
        {
            HealthVerdict.Excellent => CheckState.Pass,
            HealthVerdict.Ok => CheckState.Warn,
            _ => CheckState.Fail,
        };
        LastCheckTitle.Text = $"Health score {health.Score} · {HealthScore.Label(health.Verdict)}";
        var outcome = new List<string>();
        if (failed > 0) outcome.Add($"{failed} failed");
        if (warned > 0) outcome.Add($"{warned} with warnings");
        LastCheckDetail.Text = $"{tested} test{(tested == 1 ? "" : "s")} run this session, " +
                               (outcome.Count == 0 ? "all passed." : string.Join(", ", outcome) + ".") +
                               (health.NotTested.Count > 0 ? $" {health.NotTested.Count} not tested yet." : "");
    }

    private void ViewReport_Click(object sender, RoutedEventArgs e) => _window.ShowManual("report");

    public int TileColumns { get => (int)GetValue(TileColumnsProperty); set => SetValue(TileColumnsProperty, value); }

    public StressPreset SelectedPreset =>
        QuickRadio.IsChecked == true ? StressPreset.Quick : ExtendedRadio.IsChecked == true ? StressPreset.Extended : StressPreset.Standard;

    private void Duration_Checked(object sender, RoutedEventArgs e)
    {
        if (TotalEstimate is null) return;   // fires during InitializeComponent
        var total = SelectedPreset.Duration() + StressPresets.HandsOnEstimate;
        TotalEstimate.Text = $"About {Format.Minutes(total)} in total, including the hands-on tests.";
    }

    private void StartAutomatic_Click(object sender, RoutedEventArgs e) => _window.StartAutomatic(SelectedPreset);

    private void OpenManual_Click(object sender, RoutedEventArgs e) => _window.ShowManual();

    private void Win11Details_Click(object sender, RoutedEventArgs e) => _window.ShowWin11();

    private void CopySerial_Click(object sender, RoutedEventArgs e)
    {
        if (_model.Serial is not { } serial) return;
        try
        {
            Clipboard.SetText(serial);
            _window.Notify("Copied", serial, ControlAppearance.Secondary, SymbolRegular.Copy24);
        }
        catch (System.Runtime.InteropServices.COMException) { }   // clipboard held by another app
    }

    private void RestartElevated_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception) { }   // UAC prompt declined
    }

    // ---------- PawnIO sensor driver ----------

    private void SetUpSensorBanner()
    {
        // Installing needs admin; in limited mode the admin banner already covers it.
        var state = PawnIoSetup.State;
        if (state == PawnIoState.Installed || !_snapshot.IsAdmin) return;
        SensorBanner.Visibility = Visibility.Visible;
        if (state == PawnIoState.Outdated)
        {
            SensorTitle.Text = "Update the sensor driver";
            SensorMessage.Text = $"The installed PawnIO driver ({PawnIoSetup.InstalledVersion()}) is too old for exact temperature readings.";
            InstallSensorButton.Content = "Update driver";
        }
    }

    private async void InstallSensor_Click(object sender, RoutedEventArgs e)
    {
        InstallSensorButton.IsEnabled = false;
        InstallSensorButton.Content = "Installing…";

        // The setup is a separate program (antivirus can block it) and LibreHardwareMonitor can throw
        // anything while reopening; whatever happens, the button must not stay stuck on "Installing…".
        PawnIoSetupResult result;
        try { result = await Task.Run(PawnIoSetup.InstallAsync); }
        catch (Exception ex)
        {
            App.LogError(ex);
            result = new(false, $"The sensor driver couldn't be installed: {ex.Message}");
        }
        if (!result.Success)
        {
            SensorMessage.Text = result.Message;
            InstallSensorButton.Content = "Try again";
            InstallSensorButton.IsEnabled = true;
            return;
        }

        // Re-attach the sensors so the new driver is used right away.
        SensorReading? reading;
        try
        {
            reading = await Task.Run(() =>
            {
                var sensors = _window.Sensors;
                if (sensors is null)
                {
                    sensors = new SensorService();
                    try { sensors.Open(); }
                    catch { sensors.Dispose(); throw; }
                    _window.Sensors = sensors;
                }
                else sensors.ReopenHardware();
                return sensors.Read();
            });
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            reading = null;
        }

        SensorBanner.SetResourceReference(StyleProperty, "Diag.Banner.Pass");
        SensorIcon.Symbol = SymbolRegular.CheckmarkCircle24;
        SensorIcon.SetResourceReference(SymbolIcon.ForegroundProperty, "SystemFillColorSuccessBrush");
        SensorTitle.Text = "Sensor driver installed";
        SensorMessage.Text = reading switch
        {
            { CpuTempC: { } t, CpuTempLimited: false } => $"CPU temperature now reads {t:0} °C. You'll be asked about removing the driver when you close the app.",
            null => "Restart Diagnostiq to read exact temperatures. You'll be asked about removing the driver when you close the app.",
            _ => "You'll be asked about removing the driver when you close the app.",
        };
        SensorActions.Visibility = Visibility.Collapsed;
    }

    private void DismissSensor_Click(object sender, RoutedEventArgs e) => SensorBanner.Visibility = Visibility.Collapsed;
}
