using Diagnostiq.Core;
using Diagnostiq.Core.Probing;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.Presentation;

public static class Outcomes
{
    public static string Label(TestOutcome? o) => o switch
    {
        TestOutcome.Pass => "Passed",
        TestOutcome.Warn => "Warning",
        TestOutcome.Fail => "Failed",
        TestOutcome.Skipped => "Skipped",
        _ => "Not run",
    };

    /// <summary>Pill colour; skipped and not-run stay neutral.</summary>
    public static CheckState? State(TestOutcome? o) => o switch
    {
        TestOutcome.Pass => CheckState.Pass,
        TestOutcome.Warn => CheckState.Warn,
        TestOutcome.Fail => CheckState.Fail,
        _ => null,
    };

    /// <summary>Why a probe has no value, in plain words; never the raw WMI or exception text ("Invalid class").</summary>
    public static string Unavailable<T>(ProbeResult<T> r) => r.Status switch
    {
        ProbeStatus.NeedsAdmin => "Needs administrator rights",
        ProbeStatus.TimedOut => "Didn't answer in time",
        ProbeStatus.NotAvailable => "Not available on this computer",
        _ => "Couldn't read",
    };
}
