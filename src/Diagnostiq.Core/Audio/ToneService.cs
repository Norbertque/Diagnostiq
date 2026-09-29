using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Diagnostiq.Core.Audio;

public enum ToneChannel { Left, Right, Both }

/// <summary>Plays test tones and sweeps through the default output device (speakers or headphones).</summary>
public static class ToneService
{
    private const int SampleRate = 48000;

    public static Task PlayToneAsync(ToneChannel channel, double frequencyHz = 1000, TimeSpan? duration = null, CancellationToken ct = default) =>
        PlayAsync(new SignalGenerator(SampleRate, 1) { Type = SignalGeneratorType.Sin, Frequency = frequencyHz, Gain = 0.4 },
            channel, duration ?? TimeSpan.FromSeconds(1.5), ct);

    /// <summary>Logarithmic-feeling sweep that exposes rattling or buzzing speakers.</summary>
    public static Task PlaySweepAsync(ToneChannel channel, double fromHz = 100, double toHz = 12000, TimeSpan? duration = null, CancellationToken ct = default)
    {
        var d = duration ?? TimeSpan.FromSeconds(6);
        return PlayAsync(new SignalGenerator(SampleRate, 1)
        {
            Type = SignalGeneratorType.Sweep, Frequency = fromHz, FrequencyEnd = toHz, SweepLengthSecs = d.TotalSeconds, Gain = 0.35,
        }, channel, d, ct);
    }

    private static async Task PlayAsync(ISampleProvider mono, ToneChannel channel, TimeSpan duration, CancellationToken ct)
    {
        var shaped = new EnvelopeSampleProvider(mono.Take(duration), rampMs: 25, totalSamples: (long)(duration.TotalSeconds * SampleRate));
        ISampleProvider stereo = channel == ToneChannel.Both
            ? new MonoToStereoSampleProvider(shaped)
            : new PanningSampleProvider(shaped) { PanStrategy = new LinearPanStrategy(), Pan = channel == ToneChannel.Left ? -1f : 1f };

        await PlayToEndAsync(stereo.ToWaveProvider(), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Plays a finite stream on the default output device and completes when it ends.</summary>
    internal static async Task PlayToEndAsync(IWaveProvider source, CancellationToken ct)
    {
        using var output = new WasapiPlayerBuilder().WithSharedMode().WithLatency(80).Build();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null) finished.TrySetException(e.Exception); else finished.TrySetResult();
        };
        output.Init(source);
        output.Play();
        using (ct.Register(() => output.Stop()))
            await finished.Task.ConfigureAwait(false);
    }
}

/// <summary>Linear fade in/out so tones start and stop without a click.</summary>
internal sealed class EnvelopeSampleProvider(ISampleProvider source, int rampMs, long totalSamples) : ISampleProvider
{
    private readonly long _ramp = source.WaveFormat.SampleRate * rampMs / 1000;
    private long _position;

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int n = source.Read(buffer);
        for (int i = 0; i < n; i++, _position++)
        {
            float gain = 1f;
            if (_position < _ramp) gain = (float)_position / _ramp;
            else if (_position > totalSamples - _ramp) gain = Math.Max(0f, (float)(totalSamples - _position) / _ramp);
            buffer[i] *= gain;
        }
        return n;
    }
}
