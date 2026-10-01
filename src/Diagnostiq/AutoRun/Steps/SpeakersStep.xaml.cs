using System.Windows;
using Diagnostiq.Core.Audio;
using Diagnostiq.Core.Testing;
using Wpf.Ui.Controls;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>Left, right, both and a sweep through the default output.</summary>
public partial class SpeakersStep : StepView
{
    private readonly AudioPlayerQueue _player = new();
    private (float Volume, bool Muted)? _originalVolume;

    public SpeakersStep() => InitializeComponent();

    public override string Id => TestIds.Speakers;
    public override string Title => "Speakers";

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        var output = await Task.Run(AudioDevices.DefaultOutput, ct);
        if (output is null)
        {
            DeviceText.Text = "No audio output device found.";
            Ctx.Suggest(TestOutcome.Fail);
            return;
        }
        DeviceText.Text = $"Playing through {output.Name} · volume {output.Volume:0%}{(output.Muted ? " (muted)" : "")}";
        if (output.Muted || output.Volume < 0.2f)
        {
            VolumeText.Text = output.Muted ? "The volume is muted." : $"The volume is low ({output.Volume:0%}).";
            VolumeBanner.Visibility = Visibility.Visible;
        }
        if (!Ctx.LiveDevices) return;

        _ = Task.Run(async () =>
        {
            await Play(LeftButton, t => ToneService.PlayToneAsync(ToneChannel.Left, ct: t));
            await Play(RightButton, t => ToneService.PlayToneAsync(ToneChannel.Right, ct: t));
            await Play(SweepButton, t => ToneService.PlaySweepAsync(ToneChannel.Both, ct: t));
        }, ct);
    }

    private Task Play(Button button, Func<CancellationToken, Task> sound) => _player.PlayAsync(Dispatcher, button, sound);

    private void Left_Click(object sender, RoutedEventArgs e) => _ = Play(LeftButton, t => ToneService.PlayToneAsync(ToneChannel.Left, ct: t));
    private void Right_Click(object sender, RoutedEventArgs e) => _ = Play(RightButton, t => ToneService.PlayToneAsync(ToneChannel.Right, ct: t));
    private void Both_Click(object sender, RoutedEventArgs e) => _ = Play(BothButton, t => ToneService.PlayToneAsync(ToneChannel.Both, ct: t));
    private void Sweep_Click(object sender, RoutedEventArgs e) => _ = Play(SweepButton, t => ToneService.PlaySweepAsync(ToneChannel.Both, ct: t));

    private void SetVolume_Click(object sender, RoutedEventArgs e)
    {
        var before = AudioDevices.SetDefaultOutputVolume(0.6f);
        _originalVolume ??= before;   // keep the user's own level, not one we set earlier
        VolumeBanner.Visibility = Visibility.Collapsed;
        DeviceText.Text = DeviceText.Text.Split(" · ")[0] + " · volume 60% (restored afterwards)";
    }

    protected override string? Detail(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Pass => "Left, right and the sweep sounded clean.",
        TestOutcome.Fail => "Speaker problem reported: wrong side, silent, crackling or rattling.",
        _ => null,
    };

    public override void Cleanup()
    {
        _player.Stop();
        if (_originalVolume is not { } v) return;
        _ = Task.Run(() =>   // an audio-service call: keep it off the UI thread
        {
            try { AudioDevices.RestoreDefaultOutputVolume(v); }
            catch (System.Runtime.InteropServices.COMException) { }   // the device is gone: nothing to restore
        });
    }
}

/// <summary>
/// Plays one sound at a time; a new request stops the current one. Highlights the button that's playing.
/// <see cref="Stop"/> is final, so a queued sequence (left, right, sweep) can't carry on into the next step.
/// </summary>
public sealed class AudioPlayerQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _current;
    private volatile bool _stopped;

    public async Task PlayAsync(System.Windows.Threading.Dispatcher ui, Button button, Func<CancellationToken, Task> sound)
    {
        if (_stopped) return;
        _current?.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        if (_stopped) { _gate.Release(); return; }
        var cts = _current = new CancellationTokenSource();
        if (_stopped) cts.Cancel();   // Stop() ran between the check and taking over _current
        try
        {
            await ui.InvokeAsync(() => button.Appearance = ControlAppearance.Primary);
            await sound(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }   // device vanished mid-play
        finally
        {
            await ui.InvokeAsync(() => button.Appearance = ControlAppearance.Secondary);
            _gate.Release();
        }
    }

    public void Stop()
    {
        _stopped = true;
        _current?.Cancel();
    }
}
