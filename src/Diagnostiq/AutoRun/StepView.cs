using System.Windows.Controls;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun;

public enum StepMode
{
    /// <summary>The user decides: Pass / Fail / Skip in the top bar.</summary>
    Judged,
    /// <summary>Runs by itself and records its own results; only Skip is offered.</summary>
    Automatic,
}

/// <summary>
/// One screen of the Automatic run. Judged steps show something, then wait for the user's
/// verdict (or call <see cref="AutoRunContext.Judge"/> themselves when the result is obvious,
/// e.g. every key pressed). Automatic steps override <see cref="OnRunAsync"/>.
/// </summary>
public abstract class StepView : UserControl
{
    /// <summary>Primary result id (see <see cref="TestIds"/>).</summary>
    public abstract string Id { get; }
    public abstract string Title { get; }
    public virtual StepMode Mode => StepMode.Judged;

    /// <summary>Results this step writes; any still missing when the user skips are recorded as skipped.</summary>
    public virtual IReadOnlyList<string> ResultIds => [Id];

    /// <summary>E.g. the charger step on a machine without a battery.</summary>
    public virtual bool IsApplicable(AutoRunContext ctx) => true;

    protected AutoRunContext Ctx { get; private set; } = null!;

    public Task RunAsync(AutoRunContext ctx, CancellationToken ct)
    {
        Ctx = ctx;
        return OnRunAsync(ct);
    }

    protected virtual async Task OnRunAsync(CancellationToken ct)
    {
        await OnStartAsync(ct);
        var outcome = await Ctx.WaitForJudgementAsync(ct);
        Ctx.Run.Record(new TestResult(Id, Title, outcome, Detail(outcome)));
    }

    /// <summary>Judged steps: set things up (open the camera, install the hook, …).</summary>
    protected virtual Task OnStartAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Detail recorded with the user's verdict ("84 of 84 keys worked").</summary>
    protected virtual string? Detail(TestOutcome outcome) => null;

    /// <summary>Always called when the step ends, however it ends. Release hooks, devices, audio.</summary>
    public virtual void Cleanup() { }
}
