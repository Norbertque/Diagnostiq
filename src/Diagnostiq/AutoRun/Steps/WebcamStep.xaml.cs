using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AForge.Video;
using AForge.Video.DirectShow;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>Live preview from the built-in camera (DirectShow through AForge), shown mirrored like a selfie view.</summary>
public partial class WebcamStep : StepView
{
    private List<FilterInfo> _cameras = [];
    private int _index;
    private VideoCaptureDevice? _device;
    private int _frames;
    private long _lastShown;
    private string? _format;
    private System.Windows.Threading.DispatcherTimer? _noFrames;
    private int _generation;

    public WebcamStep() => InitializeComponent();

    public override string Id => TestIds.Webcam;
    public override string Title => "Camera";

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        if (!Ctx.LiveDevices) { Overlay.Text = "Camera preview"; return; }

        // DirectShow enumeration and start-up can stall on a slow or stuck driver: keep them off the UI thread.
        // Windows Hello IR cameras also enumerate; they show a dark, grainy picture, so list them last.
        Overlay.Text = "Looking for a camera…";
        var cameras = await Task.Run(() => new FilterInfoCollection(FilterCategory.VideoInputDevice).Cast<FilterInfo>()
            .OrderBy(c => c.Name.Contains("IR", StringComparison.Ordinal) ? 1 : 0).ToList(), CancellationToken.None);
        if (ct.IsCancellationRequested) return;
        _cameras = cameras;
        if (_cameras.Count == 0)
        {
            Overlay.Text = "No camera found. It may be switched off in BIOS or missing a driver.";
            DeviceText.Text = "Windows doesn't report a camera.";
            Ctx.Suggest(TestOutcome.Fail);
            return;
        }
        SwitchButton.Visibility = _cameras.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        await OpenAsync(0);
    }

    /// <summary>
    /// Opens a camera in the background. If the step ended or another camera was chosen meanwhile
    /// (<see cref="_generation"/> moved on), the camera that just started is stopped again at once.
    /// </summary>
    private async Task OpenAsync(int index)
    {
        Close();
        int generation = _generation;
        _index = index;
        _frames = 0;
        var info = _cameras[index];
        Overlay.Text = "Starting the camera…";
        Overlay.Visibility = Visibility.Visible;

        var (device, format) = await Task.Run(() =>
        {
            var d = new VideoCaptureDevice(info.MonikerString);
            // A 720p-class mode is plenty to judge the picture and cheap for old laptops.
            var modes = d.VideoCapabilities;   // builds a DirectShow graph the first time
            var mode = modes.Where(m => m.FrameSize.Width <= 1920).OrderByDescending(m => m.FrameSize.Width <= 1280)
                            .ThenByDescending(m => m.FrameSize.Width).ThenByDescending(m => m.AverageFrameRate).FirstOrDefault()
                       ?? modes.FirstOrDefault();
            if (mode is not null) d.VideoResolution = mode;
            d.NewFrame += OnFrame;
            d.VideoSourceError += (_, e) => Dispatcher.BeginInvoke(() => Overlay.Text = $"The camera reported an error: {e.Description}");
            d.Start();
            return (d, mode is null ? null : $"{mode.FrameSize.Width} × {mode.FrameSize.Height}, {mode.AverageFrameRate} fps");
        });
        if (generation != _generation) { Stop(device); return; }   // skipped, or switched again, while starting

        _device = device;
        _format = format;
        DeviceText.Text = $"{info.Name}{(_format is null ? "" : $" · {_format}")}";
        _noFrames?.Stop();
        _noFrames = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _noFrames.Tick += (_, _) =>
        {
            _noFrames.Stop();
            if (_frames == 0)
                Overlay.Text = "No picture. Open the privacy shutter, check for a camera on/off key, and make sure " +
                               "Settings › Privacy & security › Camera lets desktop apps use the camera.";
        };
        _noFrames.Start();
    }

    /// <summary>Runs on the capture thread; copies the frame before AForge reuses it, shows at most ~20 fps.</summary>
    private void OnFrame(object sender, NewFrameEventArgs e)
    {
        Interlocked.Increment(ref _frames);
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastShown) < 50) return;
        Interlocked.Exchange(ref _lastShown, now);

        var frame = e.Frame;
        var rect = new System.Drawing.Rectangle(0, 0, frame.Width, frame.Height);
        var data = frame.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            var image = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null,
                data.Scan0, data.Stride * frame.Height, data.Stride);
            image.Freeze();
            Dispatcher.BeginInvoke(() =>
            {
                if (_device is null) return;   // queued before the step ended: leave the next step's top bar alone
                Preview.Source = image;
                if (Overlay.Visibility == Visibility.Visible) { Overlay.Visibility = Visibility.Collapsed; Ctx.Suggest(null); }
            });
        }
        finally { frame.UnlockBits(data); }
    }

    private async void Switch_Click(object sender, RoutedEventArgs e)
    {
        try { await OpenAsync((_index + 1) % _cameras.Count); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ApplicationException)
        {
            Overlay.Text = $"That camera couldn't be opened: {ex.Message}";
        }
    }

    private void Close()
    {
        _generation++;   // a camera still starting in the background is stopped as soon as it's up
        _noFrames?.Stop();
        if (_device is null) return;
        Stop(_device);
        _device = null;
    }

    private void Stop(VideoCaptureDevice device)
    {
        device.NewFrame -= OnFrame;
        device.SignalToStop();
        Task.Run(device.WaitForStop);   // can take a moment; don't block the UI
    }

    protected override string? Detail(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Pass => $"Picture looks good{(_format is null ? "" : $" ({_format})")}.",
        TestOutcome.Fail => _cameras.Count == 0 ? "No camera found."
                          : _frames == 0 ? "The camera didn't deliver a picture."
                          : "Picture problem reported (blurry, dark spots, lines or wrong colours).",
        _ => null,
    };

    public override void Cleanup() => Close();
}
