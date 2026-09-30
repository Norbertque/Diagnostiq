using Diagnostiq.Core.Audio;
using Diagnostiq.Core.Hardware;
using Diagnostiq.Core.Network;
using NAudio.Wave;

namespace Diagnostiq.Core.Tests;

public class ModelTests
{
    [Theory]
    [InlineData(50000, 45000, 90.0)]
    [InlineData(50000, 52000, 100.0)]   // new batteries can exceed design; cap at 100
    public void Battery_health_is_full_over_design(long design, long full, double expected)
    {
        var b = new BatteryInfo(true, 1, 80, true, design, full, 120);
        Assert.Equal(expected, b.HealthPercent!.Value, 1);
    }

    [Fact]
    public void Battery_health_unknown_without_capacities()
    {
        Assert.Null(new BatteryInfo(true, 1, 80, true, null, 40000, null).HealthPercent);
    }

    [Theory]
    [InlineData("2.0, 0, 1.38", 2.0)]
    [InlineData("1.2, 2, 3", 1.2)]
    public void Tpm_major_version_parsed(string spec, double expected)
    {
        Assert.Equal(expected, new TpmInfo(true, null, spec, true, true, null).MajorVersion);
    }

    [Theory]
    [InlineData("192.168.1.20", true)]
    [InlineData("169.254.3.4", false)]   // APIPA = no DHCP answer = not really connected
    public void Adapter_connected_only_with_real_address(string ip, bool connected)
    {
        var a = new NetworkAdapterInfo(AdapterKind.WiFi, "Wi-Fi", "Intel", true, ip, "192.168.1.1", 1, null);
        Assert.Equal(connected, a.IsConnected);
    }

    [Fact]
    public void Mic_peak_from_float_samples()
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(0.1f).CopyTo(bytes, 0);
        BitConverter.GetBytes(-0.6f).CopyTo(bytes, 4);
        Assert.Equal(0.6f, MicRecorder.Peak(bytes, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)), 3);
    }

    [Fact]
    public void Mic_peak_from_16bit_samples()
    {
        var bytes = new byte[4];
        BitConverter.GetBytes((short)16384).CopyTo(bytes, 0);
        Assert.Equal(0.5f, MicRecorder.Peak(bytes, new WaveFormat(48000, 16, 1)), 3);
    }
}
