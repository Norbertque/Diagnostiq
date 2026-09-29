using Diagnostiq.Core.Probing;

namespace Diagnostiq.Core.Tests;

public class ProbeTests
{
    [Fact]
    public async Task Value_is_Ok()
    {
        var r = await Probe.RunAsync(() => "x");
        Assert.Equal(ProbeStatus.Ok, r.Status);
        Assert.Equal("x", r.Value);
    }

    [Fact]
    public async Task Null_is_NotAvailable()
    {
        var r = await Probe.RunAsync<string>(() => null);
        Assert.Equal(ProbeStatus.NotAvailable, r.Status);
    }

    [Fact]
    public async Task Hung_query_times_out_without_waiting_for_it()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await Probe.RunAsync(() => { Thread.Sleep(5000); return "late"; }, TimeSpan.FromMilliseconds(200));
        Assert.Equal(ProbeStatus.TimedOut, r.Status);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Access_denied_is_NeedsAdmin()
    {
        var r = await Probe.RunAsync<string>(() => throw new UnauthorizedAccessException());
        Assert.Equal(ProbeStatus.NeedsAdmin, r.Status);
    }

    [Fact]
    public async Task Other_exceptions_are_Error_with_message()
    {
        var r = await Probe.RunAsync<string>(() => throw new InvalidOperationException("boom"));
        Assert.Equal(ProbeStatus.Error, r.Status);
        Assert.Equal("boom", r.Message);
    }
}
