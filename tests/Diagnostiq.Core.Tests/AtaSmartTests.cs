using Diagnostiq.Core.Storage;

namespace Diagnostiq.Core.Tests;

public class AtaSmartTests
{
    // Builds a 362-byte SMART block (2-byte header + 30 × 12-byte entries).
    private static byte[] Block(params (byte id, byte cur, byte worst, long raw)[] attrs)
    {
        var data = new byte[2 + 30 * 12];
        for (int k = 0; k < attrs.Length; k++)
        {
            int i = 2 + k * 12;
            data[i] = attrs[k].id;
            data[i + 3] = attrs[k].cur;
            data[i + 4] = attrs[k].worst;
            for (int j = 0; j < 6; j++) data[i + 5 + j] = (byte)(attrs[k].raw >> (8 * j));
        }
        return data;
    }

    private static byte[] Thresholds(params (byte id, byte thr)[] t)
    {
        var data = new byte[2 + 30 * 12];
        for (int k = 0; k < t.Length; k++) { data[2 + k * 12] = t[k].id; data[3 + k * 12] = t[k].thr; }
        return data;
    }

    [Fact]
    public void Parses_ids_values_raw_and_thresholds()
    {
        var attrs = AtaSmart.Parse(Block((0x09, 98, 98, 12345), (0x05, 100, 100, 0)), Thresholds((0x05, 10)));
        Assert.Equal(2, attrs.Count);
        var hours = attrs.Single(a => a.Id == 0x09);
        Assert.Equal("Power-on hours", hours.Name);
        Assert.Equal(12345, hours.Raw);
        Assert.Equal(10, attrs.Single(a => a.Id == 0x05).Threshold);
    }

    [Fact]
    public void Healthy_drive_has_no_problems()
    {
        var drive = new SmartDrive("d", false, AtaSmart.Parse(Block((0x05, 100, 100, 0), (0xC5, 100, 100, 0)), null));
        Assert.True(drive.Healthy);
    }

    [Fact]
    public void Reallocated_or_pending_sectors_are_problems()
    {
        var drive = new SmartDrive("d", false, AtaSmart.Parse(Block((0x05, 100, 100, 8), (0xC5, 100, 100, 2)), null));
        Assert.Contains(drive.Problems, p => p.StartsWith("Reallocated sectors"));
        Assert.Contains(drive.Problems, p => p.StartsWith("Current pending sectors"));
    }

    [Fact]
    public void Value_at_threshold_fails()
    {
        var drive = new SmartDrive("d", false, AtaSmart.Parse(Block((0x01, 16, 16, 0)), Thresholds((0x01, 16))));
        Assert.Contains(drive.Problems, p => p.Contains("below failure threshold"));
    }

    [Fact]
    public void Failure_prediction_is_a_problem()
    {
        Assert.False(new SmartDrive("d", true, []).Healthy);
    }
}
