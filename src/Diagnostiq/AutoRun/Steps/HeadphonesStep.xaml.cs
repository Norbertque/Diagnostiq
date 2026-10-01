using System.Runtime.InteropServices;
using System.Windows;
using Diagnostiq.Core;
using Diagnostiq.Core.Audio;
using Diagnostiq.Core.Testing;
using NAudio.CoreAudioApi;
using Wpf.Ui.Controls;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>
/// Waits for Windows to switch to a headphone endpoint (jack detection), then plays left and
/// right through it. Laptops with a single combined endpoint don't report the plug; the tones
/// still play through whatever is connected.
/// </summary>
public partial class HeadphonesStep : StepView
{
    private readonly AudioPlayerQueue _player = new();
    private MMDeviceEnumerator? _devices;
    private MMDeviceNotificationClient? _notifications;
    private bool _detected;

    public HeadphonesStep() => InitializeComponent();

    public override string Id => TestIds.Headphones;
    public override string Title => "Headphone jack";

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        if (!Ctx.LiveDevices) return;
        // Audio-service calls stay off the UI thread; notifications arrive on the audio worker thread
        // and are handed over without waiting.
        var (devices, notifications) = await Task.Run(() =>
        {
            var d = new MMDeviceEnumerator();
            return (d, d.CreateNotificationClient(useSynchronizationContext: false));
        }, CancellationToken.None);
        if (ct.IsCancellationRequested) { _ = Task.Run(() => { notifications.Dispose(); devices.Dispose(); }); return; }
        _devices = devices;
        _notifications = notifications;
        notifications.DefaultDeviceChanged += (_, _) => Dispatcher.BeginInvoke(Check);
        notifications.DeviceStateChanged += (_, _) => Dispatcher.BeginInvoke(Check);
        notifications.PropertyValueChanged += (_, _) => Dispatcher.BeginInvoke(Check);
        Check();
    }

    private async void Check()
    {
        if (_detected || _devices is null) return;   // already found, or a notification queued before the step ended
        // The endpoint may be mid-change; the next notification tries again.
        var output = await Task.Run(() => { try { return AudioDevices.DefaultOutput(); } catch (COMException) { return null; } });
        if (_detected || _devices is null) return;   // found meanwhile, or the step ended
        if (output is not { IsHeadphones: true }) return;
        _detected = true;
        JackIcon.State = CheckState.Pass;
        JackTitle.Text = "Headphones detected";
        JackText.Text = $"Playing through {output.Name}.";
        _ = Task.Run(async () =>
        {
            await Play(LeftButton, t => ToneService.PlayToneAsync(ToneChannel.Left, ct: t));
            await Play(RightButton, t => ToneService.PlayToneAsync(ToneChannel.Right, ct: t));
        });
    }

    private Task Play(Button button, Func<CancellationToken, Task> sound) => _player.PlayAsync(Dispatcher, button, sound);

    private void Left_Click(object sender, RoutedEventArgs e) => _ = Play(LeftButton, t => ToneService.PlayToneAsync(ToneChannel.Left, ct: t));
    private void Right_Click(object sender, RoutedEventArgs e) => _ = Play(RightButton, t => ToneService.PlayToneAsync(ToneChannel.Right, ct: t));
    private void Sweep_Click(object sender, RoutedEventArgs e) => _ = Play(SweepButton, t => ToneService.PlaySweepAsync(ToneChannel.Both, ct: t));

    protected override string? Detail(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Pass => _detected ? "Plug detected; left and right play correctly." : "Left and right play correctly (the plug isn't reported to Windows).",
        TestOutcome.Fail => _detected ? "Plug detected, but the sound was wrong." : "Headphones weren't detected or didn't play.",
        _ => null,
    };

    public override void Cleanup()
    {
        _player.Stop();
        var (devices, notifications) = (_devices, _notifications);
        _notifications = null;
        _devices = null;
        _ = Task.Run(() => { notifications?.Dispose(); devices?.Dispose(); });   // unregistering waits on the audio service
    }
}
