using Diagnostiq.Core.Win11;

namespace Diagnostiq.Core.Tests;

public class SupportedCpusTests
{
    [Theory]
    [InlineData("12th Gen Intel(R) Core(TM) i7-1265U")]
    [InlineData("Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz")]
    [InlineData("Intel(R) Core(TM) i7-10510U CPU @ 1.80GHz")]
    [InlineData("Intel(R) Core(TM) i7-1065G7 CPU @ 1.30GHz")]
    [InlineData("11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz")]
    [InlineData("13th Gen Intel(R) Core(TM) i9-13900HX")]
    [InlineData("Intel(R) Core(TM) i9-14900K")]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H")]
    [InlineData("Intel(R) Core(TM) Ultra 7 258V")]
    [InlineData("Intel(R) Core(TM) 7 150U")]
    [InlineData("Intel(R) Core(TM) m3-8100Y CPU @ 1.10GHz")]
    [InlineData("Intel(R) Celeron(R) N4020 CPU @ 1.10GHz")]
    [InlineData("Intel(R) Celeron(R) 4205U CPU @ 1.80GHz")]
    [InlineData("Intel(R) Pentium(R) Silver N6000 @ 1.10GHz")]
    [InlineData("Intel(R) Pentium(R) Gold 6405U CPU @ 2.40GHz")]
    [InlineData("Intel(R) N100")]
    [InlineData("Intel(R) Core(TM) i3-N305")]
    [InlineData("Intel(R) Xeon(R) W-10855M CPU @ 2.80GHz")]
    [InlineData("Intel(R) Xeon(R) E-2176M CPU @ 2.70GHz")]
    [InlineData("AMD Ryzen 5 PRO 3500U w/ Radeon Vega Mobile Gfx")]
    [InlineData("AMD Ryzen 7 5800H with Radeon Graphics")]
    [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics")]
    [InlineData("AMD Ryzen AI 9 HX 370 w/ Radeon 890M")]
    [InlineData("AMD Ryzen 5 2600 Six-Core Processor")]
    [InlineData("AMD Athlon Silver 3050U with Radeon Graphics")]
    [InlineData("AMD 3020e with Radeon Graphics")]
    [InlineData("Snapdragon(R) X Elite - X1E80100 - Qualcomm(R) Oryon(TM) CPU")]
    [InlineData("Snapdragon (TM) 8cx @ 2.84 GHz")]
    public void Supported_processors(string name) => Assert.Equal(CpuSupport.Supported, SupportedCpus.Check(name).Support);

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-7600U CPU @ 2.80GHz")]
    [InlineData("Intel(R) Core(TM) i7-7820HQ CPU @ 2.90GHz")]
    [InlineData("Intel(R) Core(TM) i5-6300U CPU @ 2.40GHz")]
    [InlineData("Intel(R) Core(TM) i7-4600U CPU @ 2.10GHz")]
    [InlineData("Intel(R) Core(TM) i7 CPU 920 @ 2.67GHz")]
    [InlineData("Intel(R) Core(TM) m3-7Y30 CPU @ 1.00GHz")]
    [InlineData("Intel(R) Core(TM)2 Duo CPU T7700 @ 2.40GHz")]
    [InlineData("Intel(R) Celeron(R) CPU N3350 @ 1.10GHz")]
    [InlineData("Intel(R) Pentium(R) CPU G4560 @ 3.50GHz")]
    [InlineData("AMD Ryzen 7 2700U with Radeon Vega Mobile Gfx")]
    [InlineData("AMD Ryzen 5 1600 Six-Core Processor")]
    [InlineData("AMD A10-9600P RADEON R5, 10 COMPUTE CORES 4C+6G")]
    [InlineData("Qualcomm Snapdragon 835")]
    public void Unsupported_processors(string name) => Assert.Equal(CpuSupport.NotSupported, SupportedCpus.Check(name).Support);

    [Fact]
    public void Newer_than_the_list_is_likely_supported() =>
        Assert.Equal(CpuSupport.LikelySupported, SupportedCpus.Check("Intel(R) Core(TM) Ultra 7 455H").Support);

    [Theory]
    [InlineData("Common KVM processor")]
    [InlineData("")]
    public void Unrecognised_names_are_unknown(string name) => Assert.Equal(CpuSupport.Unknown, SupportedCpus.Check(name).Support);

    [Fact]
    public void Family_names_generation()
    {
        Assert.Equal("Intel Core i7 (12th Gen)", SupportedCpus.Check("12th Gen Intel(R) Core(TM) i7-1265U").Family);
        Assert.Equal("AMD Ryzen 5 PRO 3500U", SupportedCpus.Check("AMD Ryzen 5 PRO 3500U w/ Radeon Vega Mobile Gfx").Family);
    }

    [Fact]
    public void Clean_strips_trademarks_and_clock() =>
        Assert.Equal("Intel Core i5-8250U", SupportedCpus.Clean("Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz"));
}
