using System.IO;
using Diagnostiq.Core.Storage;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>Runs after the stress test so the numbers aren't skewed by the surface scan.</summary>
public partial class DiskSpeedStep : StepView
{
    public DiskSpeedStep()
    {
        InitializeComponent();
        foreach (var card in new[] { ReadCard, WriteCard, RandomCard })
        {
            card.Set("–");
            card.Spark.Visibility = System.Windows.Visibility.Collapsed;
        }
    }

    public override string Id => TestIds.DiskSpeed;
    public override string Title => "Disk speed";
    public override StepMode Mode => StepMode.Automatic;

    protected override async Task OnRunAsync(CancellationToken ct)
    {
        var progress = new Progress<BenchmarkProgress>(p =>
        {
            Bar.Value = p.Fraction;
            Phase.Text = p.Phase;
        });
        var result = await DiskBenchmark.RunAsync(Path.GetTempPath(), 1024, progress, ct);

        Bar.Value = 1;
        Phase.Text = "Done";
        ReadCard.Set($"{result.SeqReadMBps:N0} MB/s");
        WriteCard.Set($"{result.SeqWriteMBps:N0} MB/s");
        RandomCard.Set($"{result.RandomRead4kIops:N0}", "reads per second");

        Ctx.Run.Benchmark = result;
        Ctx.Run.Record(Evaluate.DiskSpeed(result));
        await Task.Delay(TimeSpan.FromSeconds(2.5), ct);
    }
}
