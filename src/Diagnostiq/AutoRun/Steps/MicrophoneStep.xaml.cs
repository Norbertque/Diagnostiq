using System.Windows;
using System.Windows.Controls;
using Diagnostiq.Core;
using Diagnostiq.Core.Audio;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>Live level meter, then a 3-second recording played back through the speakers.</summary>
public partial class MicrophoneStep : StepView
{
    private const float HeardThreshold = 0.1f;
    private static readonly TimeSpan RecordLength = TimeSpan.FromSeconds(3);

    private MicRecorder? _mic;
    private float _shownLevel;
    private bool _heard, _playedBack, _silentRecording;
    private System.Windows.Threading.DispatcherTimer? _silenceHint;
    private CancellationTokenSource? _stopPlayback;

    public MicrophoneStep() => InitializeComponent();

    public override string Id => TestIds.Microphone;
    public override string Title => "Microphone";

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        if (!Ctx.LiveDevices) return;
        // Opening a microphone (a Bluetooth headset switching profile, a driver waking up) can take a
        // while: do it off the UI thread, and close it again if the step was skipped meanwhile.
        var mic = new MicRecorder();
        mic.LevelChanged += level => Dispatcher.BeginInvoke(() => OnLevel(level));
        try { await Task.Run(mic.Start, CancellationToken.None); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _ = Task.Run(mic.Dispose);
            if (ct.IsCancellationRequested) return;
            DeviceText.Text = "No working microphone found.";
            HearsIcon.State = CheckState.Fail;
            HearsText.Text = "Windows doesn't report a microphone.";
            RecordButton.IsEnabled = false;
            Ctx.Suggest(TestOutcome.Fail);
            return;
        }
        if (ct.IsCancellationRequested) { _ = Task.Run(mic.Dispose); return; }
        _mic = mic;
        DeviceText.Text = $"Listening with {mic.DeviceName}";

        _silenceHint = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _silenceHint.Tick += (_, _) =>
        {
            _silenceHint.Stop();
            if (!_heard)
                HearsText.Text = "Nothing heard yet. Check the microphone isn't muted (often an Fn key with a crossed-out mic) and that " +
                                 "Settings › Privacy & security › Microphone lets desktop apps use it.";
        };
        _silenceHint.Start();
    }

    private void OnLevel(float level)
    {
        if (_mic is null) return;   // queued before the step ended: must not suggest a verdict for the next one
        // Fast attack, slow release, so short sounds stay visible.
        _shownLevel = level > _shownLevel ? level : _shownLevel * 0.85f;
        if (LevelFill.Parent is Border track) LevelFill.Width = Math.Min(_shownLevel * 2.5, 1) * track.ActualWidth;

        if (!_heard && level >= HeardThreshold)
        {
            _heard = true;
            HearsIcon.State = CheckState.Pass;
            HearsText.Text = "Yes.";
            SuggestIfDone();
        }
    }

    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        // Pass, Fail or Skip can end the step (and dispose the recorder) during either await.
        var mic = _mic;
        if (mic is null) return;
        RecordButton.IsEnabled = false;
        RecordButton.Content = "Recording…";
        RecordIcon.State = null;
        await mic.RecordAsync(RecordLength);
        if (_mic != mic) return;

        _silentRecording = mic.RecordedPeak < MicRecorder.SilenceThreshold;
        RecordButton.Content = "Playing back…";
        RecordText.Text = "Playing back…";
        _stopPlayback = new CancellationTokenSource();
        try { await mic.PlayBackAsync(_stopPlayback.Token); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                                      or ObjectDisposedException or OperationCanceledException) { }
        if (_mic != mic) return;

        _playedBack = true;
        RecordIcon.State = _silentRecording ? CheckState.Warn : CheckState.Pass;
        RecordText.Text = _silentRecording ? "The recording was silent." : "Did it sound clear?";
        RecordButton.Content = "Record again";
        RecordButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        RecordButton.IsEnabled = true;
        SuggestIfDone();
    }

    private void SuggestIfDone()
    {
        if (_heard && _playedBack && !_silentRecording) Ctx.Suggest(TestOutcome.Pass);
        else if (_playedBack && _silentRecording) Ctx.Suggest(TestOutcome.Fail);
    }

    protected override string? Detail(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Pass => _playedBack ? "Picks up sound; the recording played back clearly." : "Picks up sound.",
        TestOutcome.Fail => _mic is null ? "No working microphone found."
                          : !_heard ? "The microphone didn't pick up any sound."
                          : "Recording problem reported (silent or distorted).",
        _ => null,
    };

    public override void Cleanup()
    {
        _silenceHint?.Stop();
        _stopPlayback?.Cancel();   // don't play the recording over the next step
        // Stopping waits for the capture thread: never on the UI thread.
        if (_mic is { } mic) _ = Task.Run(mic.Dispose);
        _mic = null;
    }
}
