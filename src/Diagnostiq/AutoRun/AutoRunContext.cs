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
        _judgement = new TaskCompletionSource<TestOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => _judgement.TrySetCanceled(ct));
        return _judgement.Task;
    }

    /// <summary>Records the verdict for the current judged step (from the top bar or the step itself).</summary>
    public void Judge(TestOutcome outcome) => _judgement?.TrySetResult(outcome);

    public void Suggest(TestOutcome? outcome) => Suggested?.Invoke(outcome);

    /// <summary>False only for dev snapshots: steps then skip hooks and devices that would grab real input.</summary>
    public bool CaptureInput { get; set; } = true;

    /// <summary>The fullscreen window running this context (set by the window itself).</summary>
    public AutoRunWindow Window { get; internal set; } = null!;
}
