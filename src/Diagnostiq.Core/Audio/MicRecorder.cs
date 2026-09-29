using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Diagnostiq.Core.Audio;

/// <summary>
/// Default microphone: live peak level for the meter, plus record-and-play-back.
/// A recording whose peak never rises above <see cref="SilenceThreshold"/> means the
/// mic delivered only silence (muted, disabled, or broken).
/// </summary>
public sealed class MicRecorder : IDisposable
{
    public const float SilenceThreshold = 0.02f;

    private WasapiRecorder? _recorder;
    private WaveFormat? _format;
    private readonly MemoryStream _recording = new();
    private volatile bool _isRecording;
    private float _recordedPeak;

    /// <summary>Peak of the latest buffer, 0..1. Raised on the capture thread.</summary>
    public event Action<float>? LevelChanged;

    public string? DeviceName { get; private set; }
    public float RecordedPeak => _recordedPeak;
    public TimeSpan RecordedLength => _format is null ? TimeSpan.Zero
        : TimeSpan.FromSeconds((double)_recording.Length / _format.AverageBytesPerSecond);

    public static bool HasMicrophone()
    {
        using var devices = new MMDeviceEnumerator();
        return devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Any();
    }

    /// <summary>Starts the live level meter. Throws if there's no active microphone.</summary>
    public void Start()
    {
        if (_recorder is not null) return;
        using var devices = new MMDeviceEnumerator();
        var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        DeviceName = device.FriendlyName;
        _recorder = new WasapiRecorderBuilder().WithDevice(device).WithSharedMode().Build();
        _format = _recorder.WaveFormat;
        _recorder.DataAvailable += OnData;
        _recorder.StartRecording();
    }

    public async Task RecordAsync(TimeSpan length, CancellationToken ct = default)
    {
        if (_recorder is null) Start();
        lock (_recording) { _recording.SetLength(0); }
        _recordedPeak = 0;
        _isRecording = true;
        try { await Task.Delay(length, ct).ConfigureAwait(false); }
        finally { _isRecording = false; }
    }

    public async Task PlayBackAsync(CancellationToken ct = default)
    {
        if (_format is null || _recording.Length == 0) return;
        byte[] data;
        lock (_recording) { data = _recording.ToArray(); }
        using var stream = new RawSourceWaveStream(new MemoryStream(data), _format);
        await ToneService.PlayToEndAsync(stream, ct).ConfigureAwait(false);
    }

    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        // The device marks packets it knows are silent; don't bother scanning those.
        float peak = flags.HasFlag(AudioClientBufferFlags.Silent) ? 0 : Peak(buffer, _format!);
        LevelChanged?.Invoke(peak);
        if (_isRecording)
        {
            lock (_recording) { _recording.Write(buffer); }
            if (peak > _recordedPeak) _recordedPeak = peak;
        }
    }

    internal static float Peak(ReadOnlySpan<byte> bytes, WaveFormat format)
    {
        float peak = 0;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat || format.BitsPerSample == 32 && format.Encoding == WaveFormatEncoding.Extensible)
        {
            foreach (var s in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes))
                peak = Math.Max(peak, Math.Abs(s));
        }
        else if (format.BitsPerSample == 16)
        {
            foreach (var s in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(bytes))
                peak = Math.Max(peak, Math.Abs(s / 32768f));
        }
        return Math.Min(peak, 1f);
    }

    public void Dispose()
    {
        if (_recorder is not null)
        {
            _recorder.DataAvailable -= OnData;
            try { _recorder.StopRecording(); } catch (InvalidOperationException) { }
            _recorder.Dispose();
            _recorder = null;
        }
        _recording.Dispose();
    }
}
