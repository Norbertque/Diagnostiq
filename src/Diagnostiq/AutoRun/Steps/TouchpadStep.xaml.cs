using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;
using Diagnostiq.Core;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>
/// Coverage grid (dead zones on the pad show up as cells that won't fill), both buttons, and
/// two-finger scrolling. Moves on by itself once coverage, clicks and vertical scroll are done.
/// </summary>
public partial class TouchpadStep : StepView
{
    private const int Columns = 16, Rows = 9;
    private const double NeededCoverage = 0.9;
    private const int WmMouseHWheel = 0x020E;

    private readonly Rectangle[] _cells = new Rectangle[Columns * Rows];
    private readonly bool[] _visited = new bool[Columns * Rows];
    private int _visitedCount;
    private bool _left, _right, _up, _down, _sideways, _finishing;
    private HwndSource? _source;

    public TouchpadStep()
    {
        InitializeComponent();
        Cells.Columns = Columns;
        Cells.Rows = Rows;
        for (int i = 0; i < _cells.Length; i++)
        {
            // Covered cells get the success colour at 75%: strong enough that dead zones stand out as gaps.
            _cells[i] = new Rectangle { Margin = new Thickness(1), Opacity = 0.75 };
            Cells.Children.Add(_cells[i]);
        }
    }

    public override string Id => TestIds.Touchpad;
    public override string Title => "Touchpad";

    protected override Task OnStartAsync(CancellationToken ct)
    {
        // WPF has no horizontal-wheel event; listen for the raw window message.
        _source = PresentationSource.FromVisual(Ctx.Window) as HwndSource;
        _source?.AddHook(WndProc);
        Update();
        return Task.CompletedTask;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmMouseHWheel && !_sideways) { _sideways = true; Update(); }
        return 0;
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Surface);
        int col = (int)(p.X / Surface.ActualWidth * Columns), row = (int)(p.Y / Surface.ActualHeight * Rows);
        if (col is < 0 or >= Columns || row is < 0 or >= Rows) return;
        int i = row * Columns + col;
        if (_visited[i]) return;
        _visited[i] = true;
        _visitedCount++;
        _cells[i].SetResourceReference(Shape.FillProperty, "SystemFillColorSuccessBrush");
        Update();
    }

    private void Surface_Left(object sender, MouseButtonEventArgs e) { _left = true; Update(); }
    private void Surface_Right(object sender, MouseButtonEventArgs e) { _right = true; e.Handled = true; Update(); }

    private void Surface_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0) _up = true; else _down = true;
        Update();
    }

    private double CoverageFraction => (double)_visitedCount / _cells.Length;

    private void Update()
    {
        Coverage.Text = $"{CoverageFraction:0%}";
        LeftIcon.State = _left ? CheckState.Pass : null;
        RightIcon.State = _right ? CheckState.Pass : null;
        VScrollIcon.State = _up && _down ? CheckState.Pass : null;
        HScrollIcon.State = _sideways ? CheckState.Pass : null;

        bool complete = CoverageFraction >= NeededCoverage && _left && _right && _up && _down;
        if (!complete)
        {
            Status.Text = CoverageFraction < NeededCoverage ? $"Keep going: {NeededCoverage:0%} coverage needed." : "Now the buttons and scrolling.";
            return;
        }
        if (_finishing) return;
        _finishing = true;
        Status.Text = "Everything works. Moving on…";
        Ctx.Suggest(TestOutcome.Pass);
        _ = FinishSoonAsync();
    }

    private async Task FinishSoonAsync()
    {
        var judge = Ctx.JudgeCurrentStep();   // not whatever step is showing in 2 s
        await Task.Delay(TimeSpan.FromSeconds(2));
        judge(TestOutcome.Pass);
    }

    protected override string? Detail(TestOutcome outcome)
    {
        var missing = new List<string>();
        if (!_left) missing.Add("left click");
        if (!_right) missing.Add("right click");
        if (!(_up && _down)) missing.Add("vertical scroll");
        if (!_sideways) missing.Add("sideways scroll");
        string covered = $"{CoverageFraction:0%} of the surface covered";
        return missing.Count == 0 ? $"{covered}; clicks and scrolling work."
             : outcome == TestOutcome.Fail ? $"{covered}. Didn't register: {string.Join(", ", missing)}."
             : $"{covered}. Not tried: {string.Join(", ", missing)}.";
    }

    public override void Cleanup()
    {
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
