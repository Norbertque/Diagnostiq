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

    public MicrophoneStep() => InitializeComponent();

    public override string Id => TestIds.Microphone;
    public override string Title => "Microphone";

    protected override Task OnStartAsync(CancellationToken ct)
    {
        if (!Ctx.LiveDevices) return Task.CompletedTask;
        try
        {
            _mic = new MicRecorder();
            _mic.LevelChanged += level => Dispatcher.BeginInvoke(() => OnLevel(level));
            _mic.Start();
            DeviceText.Text = $"Listening with {_mic.DeviceName}";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _mic?.Dispose();
            _mic = null;
            DeviceText.Text = "No working microphone found.";
            HearsIcon.State = CheckState.Fail;
            HearsText.Text = "Windows doesn't report a microphone.";
            RecordButton.IsEnabled = false;
            Ctx.Suggest(TestOutcome.Fail);
            return Task.CompletedTask;
        }

        _silenceHint = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _silenceHint.Tick += (_, _) =>
        {
            _silenceHint.Stop();
            if (!_heard)
                HearsText.Text = "Nothing heard yet. Check the microphone isn't muted (often an Fn key with a crossed-out mic) and that " +
                                 "Settings › Privacy & security › Microphone lets desktop apps use it.";
        };
        _silenceHint.Start();
        return Task.CompletedTask;
    }

    private void OnLevel(float level)
    {
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
        if (_mic is null) return;
        RecordButton.IsEnabled = false;
        RecordButton.Content = "Recording…";
        RecordIcon.State = null;
        await _mic.RecordAsync(RecordLength);

        _silentRecording = _mic.RecordedPeak < MicRecorder.SilenceThreshold;
        RecordButton.Content = "Playing back…";
        RecordText.Text = "Playing back…";
        try { await _mic.PlayBackAsync(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }

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
        _mic?.Dispose();
        _mic = null;
    }
}
