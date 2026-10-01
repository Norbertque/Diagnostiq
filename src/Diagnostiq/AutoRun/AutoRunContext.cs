using Diagnostiq.Core;
using Diagnostiq.Core.Sensors;
using Diagnostiq.Core.Stress;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun;

/// <summary>Shared state for one Automatic run.</summary>
public sealed class AutoRunContext(SystemSnapshot snapshot, SensorService? sensors, StressPreset preset, TestRun run)
{
    private TaskCompletionSource<TestOutcome>? _judgement;

    public SystemSnapshot Snapshot { get; } = snapshot;
    public SensorService? Sensors { get; } = sensors;
    public StressPreset Preset { get; } = preset;
    public TestRun Run { get; } = run;

    /// <summary>Raised when a step suggests the obvious verdict, so the top bar can highlight it.</summary>
    public event Action<TestOutcome?>? Suggested;

    public Task<TestOutcome> WaitForJudgementAsync(CancellationToken ct)
    {
        // A step the run left behind after Skip gets here late: it mustn't take over the current step's verdict.
        if (ct.IsCancellationRequested) return Task.FromCanceled<TestOutcome>(ct);
        var judgement = new TaskCompletionSource<TestOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _judgement = judgement;
        ct.Register(() => judgement.TrySetCanceled(ct));   // this step's verdict, whichever step is current by then
        return judgement.Task;
    }

    /// <summary>Records the verdict for the current judged step (from the top bar or the step itself).</summary>
    public void Judge(TestOutcome outcome) => _judgement?.TrySetResult(outcome);

    /// <summary>
    /// Binds a later self-judgement to the verdict pending now, so it can't land on the next step
    /// (the user may click Pass or Skip before a step's own "moving on" delay runs out).
    /// </summary>
    public Action<TestOutcome> JudgeCurrentStep()
    {
        var pending = _judgement;
        return outcome => pending?.TrySetResult(outcome);
    }

    public void Suggest(TestOutcome? outcome) => Suggested?.Invoke(outcome);

    /// <summary>False only for dev snapshots: steps then skip the keyboard hook, sound, microphone and camera.</summary>
    public bool LiveDevices { get; set; } = true;

    /// <summary>The whole session (earlier manual tests), so the summary report covers everything; null in tests.</summary>
    public TestRun? Session { get; init; }

    /// <summary>The fullscreen window running this context (set by the window itself).</summary>
    public AutoRunWindow Window { get; internal set; } = null!;
}
