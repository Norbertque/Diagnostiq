using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;
using Diagnostiq.Core;
using Diagnostiq.Core.Probing;

namespace Diagnostiq.Views;

/// <summary>
/// Startup progress: one row per probe group, ticked off as each finishes (they run in
/// parallel, so rows complete in whatever order the hardware answers).
/// </summary>
public partial class LoadingView : UserControl
{
    private readonly ObservableCollection<StepRow> _rows = [];
    private readonly DispatcherTimer _slowTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public LoadingView()
    {
        InitializeComponent();
        foreach (var (id, label) in SnapshotBuilder.Steps) _rows.Add(new StepRow(id, label));
        StepList.ItemsSource = _rows;
        Bar.Maximum = _rows.Count;
        UpdateCounter();

        // Created on the UI thread, so reports arrive here already marshalled.
        Progress = new Progress<ProbeStep>(OnStep);

        _slowTimer.Tick += (_, _) =>
        {
            _slowTimer.Stop();
            var waiting = _rows.Where(r => r.State is null).Select(r => r.Label.ToLowerInvariant()).ToList();
            if (waiting.Count > 0)
                Hint.Text = $"Still waiting on: {string.Join(", ", waiting)}. Older laptops can take a little longer.";
        };
        _slowTimer.Start();
    }

    public IProgress<ProbeStep> Progress { get; }

    private void OnStep(ProbeStep step)
    {
        if (step.Status is not { } status || _rows.FirstOrDefault(r => r.Id == step.Id) is not { } row) return;
        row.Complete(status);
        UpdateCounter();
    }

    private void UpdateCounter()
    {
        int done = _rows.Count(r => r.State is not null);
        Bar.Value = done;
        Counter.Text = $"{done} of {_rows.Count}";
    }

    /// <summary>Ticks any rows whose report hasn't landed yet and pauses briefly so the finished list registers.</summary>
    public async Task FinishAsync(SystemSnapshot snapshot)
    {
        _slowTimer.Stop();
        foreach (var row in _rows.Where(r => r.State is null)) row.Complete(ProbeStatus.Ok);
        UpdateCounter();
        await Task.Delay(TimeSpan.FromMilliseconds(400));
    }

    public sealed class StepRow(string id, string label) : INotifyPropertyChanged
    {
        public string Id { get; } = id;
        public string Label { get; } = label;
        public CheckState? State { get; private set; }
        public string StatusText { get; private set; } = "Checking…";
        public string Announcement => $"{Label}: {StatusText}";

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Complete(ProbeStatus status)
        {
            (State, StatusText) = status switch
            {
                ProbeStatus.Ok => (CheckState.Pass, "Done"),
                ProbeStatus.NotAvailable => (CheckState.Pass, "Not present"),
                ProbeStatus.NeedsAdmin => (CheckState.Warn, "Needs admin"),
                ProbeStatus.TimedOut => (CheckState.Warn, "Timed out"),
                _ => (CheckState.Fail, "Failed"),
            };
            Raise(nameof(State));
            Raise(nameof(StatusText));
            Raise(nameof(Announcement));
        }

        private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
