using NAudio.CoreAudioApi;

namespace Diagnostiq.Core.Audio;

/// <param name="Volume">Master volume 0–1.</param>
/// <param name="IsHeadphones">Windows classifies the endpoint as headphones or a headset.</param>
public sealed record OutputDevice(string Id, string Name, float Volume, bool Muted, bool IsHeadphones);

public static class AudioDevices
{
    // EndpointFormFactor values from mmdeviceapi.h.
    private const uint FormFactorHeadphones = 3, FormFactorHeadset = 5;

    public static OutputDevice? DefaultOutput()
    {
        using var devices = new MMDeviceEnumerator();
        if (!devices.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return null;
        using var d = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return Describe(d);
    }

    public static bool HasMicrophone()
    {
        using var devices = new MMDeviceEnumerator();
        return devices.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }

    public static OutputDevice Describe(MMDevice d) =>
        new(d.ID, d.FriendlyName, d.AudioEndpointVolume.MasterVolumeLevelScalar, d.AudioEndpointVolume.Mute, IsHeadphones(d));

    public static bool IsHeadphones(MMDevice d)
    {
        try
        {
            var props = d.Properties;
            return props.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor)
                && props[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value is uint f && f is FormFactorHeadphones or FormFactorHeadset;
        }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }

    /// <summary>Sets the default output's volume (0–1) and unmutes it. Returns the previous state to restore later.</summary>
    public static (float Volume, bool Muted)? SetDefaultOutputVolume(float volume)
    {
        using var devices = new MMDeviceEnumerator();
        if (!devices.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return null;
        using var d = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var ep = d.AudioEndpointVolume;
        var before = (ep.MasterVolumeLevelScalar, ep.Mute);
        ep.Mute = false;
        ep.MasterVolumeLevelScalar = Math.Clamp(volume, 0, 1);
        return before;
    }

    public static void RestoreDefaultOutputVolume((float Volume, bool Muted) state)
    {
        using var devices = new MMDeviceEnumerator();
        if (!devices.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return;
        using var d = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        d.AudioEndpointVolume.MasterVolumeLevelScalar = state.Volume;
        d.AudioEndpointVolume.Mute = state.Muted;
    }
}
