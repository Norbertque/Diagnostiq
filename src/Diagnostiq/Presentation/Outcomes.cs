using Diagnostiq.Core;
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
}
