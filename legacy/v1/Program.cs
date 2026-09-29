using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;

namespace KontrolniProtokol;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.AddMessageFilter(new WheelToHoveredScroll());
        Application.Run(new MainForm());
    }
}

// Forwards mouse wheel to the AutoScroll panel under the cursor so the user
// can scroll without first clicking the scroll container.
public class WheelToHoveredScroll : System.Windows.Forms.IMessageFilter
{
    private const int WM_MOUSEWHEEL = 0x020A;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(System.Drawing.Point pt);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        var hwnd = WindowFromPoint(Cursor.Position);
        var ctrl = Control.FromHandle(hwnd);
        while (ctrl != null)
        {
            if (ctrl is ScrollableControl sc && sc.AutoScroll && sc.VerticalScroll.Visible)
            {
                SendMessage(sc.Handle, WM_MOUSEWHEEL, m.WParam, m.LParam);
                return true;
            }
            ctrl = ctrl.Parent;
        }
        return false;
    }
}

public enum TestState { Untested, Running, Pass, Fail, Info }

public class TestItem
{
    public string Category = "";
    public string Title = "";
    public string Detail = "";
    public TestState State = TestState.Untested;
    public Action<TestItem> Action;   // run interactive
    public Func<(string detail, TestState state)> AutoCheck;  // auto detect on load
    public TestCard Card;
}

// =====================================================================
// Status pill (rounded badge)
// =====================================================================
public class StatusPill : Control
{
    private TestState _state = TestState.Untested;
    public TestState State { get => _state; set { _state = value; Invalidate(); } }
    public StatusPill()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
        Size = new Size(118, 22);
        BackColor = Color.Transparent;
        Font = Theme.PillFont;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Paint corners opaque using parent's bg
        Color parentBg = Parent?.BackColor ?? Theme.Card;
        if (parentBg == Color.Transparent || parentBg.A < 255) parentBg = Theme.Card;
        using (var bgBrush = new SolidBrush(parentBg))
            g.FillRectangle(bgBrush, 0, 0, Width, Height);

        (Color bg, Color fg, string txt) = _state switch
        {
            TestState.Pass     => (Theme.SuccessSoft, Theme.Success, "● PASS"),
            TestState.Fail     => (Theme.DangerSoft, Theme.Danger, "● FAIL"),
            TestState.Running  => (Theme.WarningSoft, Theme.Warning, "● RUNNING"),
            TestState.Info     => (Color.FromArgb(219, 234, 254), Theme.Accent, "● INFO"),
            _                  => (Theme.UntestedSoft, Theme.TextMuted, "○ UNTESTED"),
        };
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Shape.Rounded(rect, Height / 2);
        using var brush = new SolidBrush(bg);
        g.FillPath(brush, path);
        TextRenderer.DrawText(g, txt, Font, rect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// =====================================================================
// TestCard — TILE-style card with rounded corners, status bar, primary action
// =====================================================================
public class TestCard : Panel
{
    public const int CardW = 280;
    public const int CardH = 168;
    private const int Radius = 10;

    private readonly Label _title;
    private readonly Label _detail;
    private readonly StatusPill _pill;
    private readonly RoundedButton _run;
    private readonly TestItem _item;

    public TestCard(TestItem item)
    {
        _item = item;
        item.Card = this;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        Width = CardW;
        Height = CardH;
        BackColor = Theme.Bg;
        Margin = new Padding(0, 0, 14, 14);

        _pill = new StatusPill { Location = new Point(18, 16), State = item.State };

        _title = new Label
        {
            Text = item.Title,
            ForeColor = Theme.Text,
            Font = Theme.TitleFont,
            Location = new Point(18, 46),
            Size = new Size(CardW - 36, 22),
            AutoEllipsis = true,
            BackColor = Color.Transparent,
        };

        _detail = new Label
        {
            Text = item.Detail,
            ForeColor = Theme.TextMuted,
            Font = Theme.BodyFont,
            Location = new Point(18, 70),
            Size = new Size(CardW - 36, 42),
            BackColor = Color.Transparent,
        };

        _run = new RoundedButton
        {
            Text = item.Action != null ? "Run test" : "Refresh",
            FillColor = Theme.Accent,
            HoverColor = Theme.AccentHover,
            ForeColor = Color.White,
            Width = CardW - 36,
            Height = 34,
            Location = new Point(18, CardH - 50),
        };
        _run.Click += (s, e) =>
        {
            if (item.Action != null) item.Action(item);
            // For auto-only tests, also allow re-running via this button
            else if (item.AutoCheck != null)
            {
                try
                {
                    var (d, st) = item.AutoCheck();
                    item.Detail = d;
                    item.State = st;
                }
                catch (Exception ex) { item.Detail = "Error: " + ex.Message; item.State = TestState.Fail; }
                Refresh();
                // Update parent MainForm stats by walking up parent chain
                Control p = Parent;
                while (p != null && p is not MainForm) p = p.Parent;
                (p as MainForm)?.RefreshStats();
            }
        };

        Controls.Add(_pill);
        Controls.Add(_title);
        Controls.Add(_detail);
        Controls.Add(_run);

        Refresh();
    }

    public new void Refresh()
    {
        _pill.State = _item.State;
        _detail.Text = _item.Detail;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Shape.Rounded(r, Radius);
        using var fill = new SolidBrush(Theme.Card);
        g.FillPath(fill, path);

        // Left status accent bar (clipped to rounded card)
        Color barColor = _item.State switch
        {
            TestState.Pass => Theme.Success,
            TestState.Fail => Theme.Danger,
            TestState.Running => Theme.Warning,
            TestState.Info => Theme.Accent,
            _ => Theme.BorderStrong
        };
        var prev = g.Clip;
        g.SetClip(path);
        using (var b2 = new SolidBrush(barColor)) g.FillRectangle(b2, 0, 0, 5, Height);
        g.Clip = prev;

        // Border
        using var border = new Pen(Theme.Border, 1);
        g.DrawPath(border, path);
    }
}

// =====================================================================
// MAIN FORM
// =====================================================================
// =====================================================================
// FOOTER — custom panel with bulletproof manual positioning
// =====================================================================
public class FooterPanel : Panel
{
    public Button LeftButton;
    public List<Button> RightButtons = new();
    public int ButtonGap = 8;
    public int SidePadding = 28;

    public FooterPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var p = new Pen(Theme.Border);
        e.Graphics.DrawLine(p, 0, 0, Width, 0);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutButtons();
    }

    public void LayoutButtons()
    {
        if (LeftButton != null)
            LeftButton.Location = new Point(SidePadding, (Height - LeftButton.Height) / 2);
        int x = Width - SidePadding;
        foreach (var b in RightButtons)
        {
            x -= b.Width;
            b.Location = new Point(x, (Height - b.Height) / 2);
            x -= ButtonGap;
        }
    }
}

public class MainForm : Form
{
    private readonly List<TestItem> _tests = new();
    private readonly List<Control> _adaptiveControls = new();   // cards/panels that should match content width
    private FlowLayoutPanel _testsFlow;
    private FlowLayoutPanel _stack;
    private Panel _scrollPanel;
    private readonly TextBox _model = MakeInput();
    private readonly TextBox _serial = MakeInput();
    private readonly TextBox _serviceTag = MakeInput();
    private readonly TextBox _tester = MakeInput();
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short };
    private readonly TextBox _notes = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.BodyFont, BorderStyle = BorderStyle.FixedSingle, Height = 90 };
    private readonly ComboBox _rating = new() { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.TitleFont, FlatStyle = FlatStyle.Flat, Height = 32 };
    private readonly List<(string Label, CheckBox Box)> _visualChecks = new();
    private readonly Label _statTotal = new() { AutoSize = true, ForeColor = Theme.Text, Font = Theme.TitleFont };
    private readonly Label _statPass = new() { AutoSize = true, ForeColor = Theme.Success, Font = Theme.TitleFont };
    private readonly Label _statFail = new() { AutoSize = true, ForeColor = Theme.Danger, Font = Theme.TitleFont };
    private readonly Label _statUntested = new() { AutoSize = true, ForeColor = Theme.Untested, Font = Theme.TitleFont };

    private static TextBox MakeInput() => new() { BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.BodyFont, BorderStyle = BorderStyle.FixedSingle };

    public MainForm()
    {
        Text = "Laptop Diagnostics";
        Width = 1520; Height = 940;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        MinimumSize = new Size(1200, 700);
        Font = Theme.BodyFont;
        Padding = new Padding(0);
        DoubleBuffered = true;

        BuildTests();

        const int ContentW = 1456;  // initial content width — will adapt to window on resize
        const int MinContentW = 1080;  // minimum (3 cards per row)

        // HEADER
        var header = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Theme.Card };
        var title = new Label { Text = "LAPTOP DIAGNOSTICS", AutoSize = true, ForeColor = Theme.Text, Font = Theme.HeaderFont, Location = new Point(36, 22) };
        var subtitle = new Label { Text = "Automatic detection and interactive tests for pre-sale check", AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.BodyFont, Location = new Point(38, 70) };
        header.Controls.Add(title); header.Controls.Add(subtitle);
        header.Paint += (s, e) =>
        {
            using var p = new Pen(Theme.Border);
            e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        // STATS BAR
        var statsPanel = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Theme.Bg, Padding = new Padding(36, 14, 36, 14) };
        var statsCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        statsCard.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, statsCard.Width - 1, statsCard.Height - 1);
            using var path = Shape.Rounded(r, 10);
            using var fill = new SolidBrush(Theme.Card);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        };
        var statsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4, RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(8, 4, 8, 4),
        };
        for (int i = 0; i < 4; i++) statsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        statsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        statsTable.Controls.Add(MakeStatTile("TOTAL", _statTotal, Theme.Text), 0, 0);
        statsTable.Controls.Add(MakeStatTile("PASS", _statPass, Theme.Success), 1, 0);
        statsTable.Controls.Add(MakeStatTile("FAIL", _statFail, Theme.Danger), 2, 0);
        statsTable.Controls.Add(MakeStatTile("UNTESTED", _statUntested, Theme.TextMuted), 3, 0);
        statsCard.Controls.Add(statsTable);
        statsPanel.Controls.Add(statsCard);

        // FOOTER — custom panel with manual positioning (bulletproof)
        var footer = new FooterPanel { Dock = DockStyle.Bottom, Height = 84, BackColor = Theme.Card };

        var btnAutoRefresh = ModernButton.MakeOutline("Re-detect", Theme.TextMuted, 130, 38);
        btnAutoRefresh.Click += (s, e) => RunAutoChecks();

        var btnExport = ModernButton.MakeOutline("Save report…", Theme.TextMuted, 140, 38);
        btnExport.Click += (s, e) => Export();

        var btnExcel = ModernButton.MakeOutline("Update Excel directly", Theme.Accent, 200, 38);
        btnExcel.Click += (s, e) => ExportToExcel();

        var btnBatch = ModernButton.Make("Batch import…", Theme.Accent, 160, 38);
        btnBatch.Click += (s, e) => { using var f = new BatchImportForm(); f.ShowDialog(this); };

        var btnSaveResult = ModernButton.Make("Save result (JSON)", Theme.Success, 190, 38);
        btnSaveResult.Click += (s, e) => SaveJsonResult();

        footer.Controls.Add(btnAutoRefresh);
        footer.Controls.Add(btnExport);
        footer.Controls.Add(btnExcel);
        footer.Controls.Add(btnBatch);
        footer.Controls.Add(btnSaveResult);
        footer.LeftButton = btnAutoRefresh;
        // Right side, painted right-to-left from the right edge:
        footer.RightButtons.Add(btnSaveResult);
        footer.RightButtons.Add(btnBatch);
        footer.RightButtons.Add(btnExcel);
        footer.RightButtons.Add(btnExport);

        // MAIN SCROLL
        _scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(36, 16, 36, 24) };
        _stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = ContentW, BackColor = Theme.Bg };

        // INFO CARD — only Model, Service Tag, Date
        _stack.Controls.Add(MakeSectionLabel("LAPTOP INFORMATION"));
        var infoCard = MakeCard(ContentW, 150);
        _adaptiveControls.Add(infoCard);
        var grid = new TableLayoutPanel { Location = new Point(24, 20), Width = ContentW - 48, Height = 110, ColumnCount = 4, BackColor = Color.Transparent, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        AddField(grid, "Model:", _model, 0, 0);
        AddField(grid, "Service Tag:", _serviceTag, 0, 2);
        AddField(grid, "Date:", _date, 1, 0);
        infoCard.Controls.Add(grid);
        _stack.Controls.Add(infoCard);

        // TESTS — ONE grid, all categories merged
        _stack.Controls.Add(MakeSectionLabel("TESTS"));
        _testsFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(ContentW, 0),
            MinimumSize = new Size(ContentW, 0),
            BackColor = Theme.Bg,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0),
        };
        foreach (var t in _tests)
        {
            var card = new TestCard(t);
            t.Card = card;
            _testsFlow.Controls.Add(card);
        }
        _stack.Controls.Add(_testsFlow);

        // VISUAL INSPECTION (manual checklist)
        _stack.Controls.Add(MakeSectionLabel("VISUAL INSPECTION (manual — check items that are OK)"));
        var vizItems = new[]
        {
            "Chassis without cracks or significant dents",
            "Display without cracks or major scratches",
            "Keyboard complete — no keys missing",
            "Touchpad without cracks or large scratches",
            "Ports mechanically OK (USB / HDMI / USB-C not loose)",
            "Display hinges firm, hold position",
            "Bottom undamaged, screws in place",
            "Battery not swollen (laptop sits flat)",
            "Service Tag label (bottom) readable",
            "Windows COA license label readable (if present)",
            "Correct Dell adapter included (65 W / 90 W)",
            "Charging LED lights up when adapter connected",
            "Fan is quiet, no grinding or clicking",
        };
        int rowsNeeded = (vizItems.Length + 1) / 2;
        var vizCard = MakeCard(ContentW, 20 + rowsNeeded * 30 + 20);
        _adaptiveControls.Add(vizCard);
        var vizGrid = new TableLayoutPanel
        {
            Location = new Point(24, 16),
            Size = new Size(ContentW - 48, rowsNeeded * 30),
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
        };
        vizGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        vizGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < rowsNeeded; i++) vizGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        int c = 0, r0 = 0;
        foreach (var item in vizItems)
        {
            var cb = new CheckBox { Text = item, AutoSize = true, ForeColor = Theme.Text, Font = Theme.BodyFont, BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(4, 6, 4, 4) };
            _visualChecks.Add((item, cb));
            vizGrid.Controls.Add(cb, c, r0);
            c++;
            if (c >= 2) { c = 0; r0++; }
        }
        vizCard.Controls.Add(vizGrid);
        _stack.Controls.Add(vizCard);

        // NOTES
        _stack.Controls.Add(MakeSectionLabel("NOTES / DEFECTS"));
        var notesCard = MakeCard(ContentW, 130);
        _adaptiveControls.Add(notesCard);
        _notes.Location = new Point(24, 16);
        _notes.Size = new Size(ContentW - 48, 100);
        _notes.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _notes.BackColor = Theme.Bg;
        _notes.BorderStyle = BorderStyle.FixedSingle;
        notesCard.Controls.Add(_notes);
        _stack.Controls.Add(notesCard);

        // RATING
        _stack.Controls.Add(MakeSectionLabel("OVERALL CONDITION  (value written to Excel inventory)"));
        var ratingCard = MakeCard(ContentW, 80);
        _adaptiveControls.Add(ratingCard);
        _rating.Items.Clear();
        _rating.Items.AddRange(new object[] { "Výborný", "Velmi dobrý", "Dobrý", "Použitelný", "Vadný / náhradní díly" });
        _rating.Location = new Point(24, 24);
        _rating.Size = new Size(ContentW - 48, 32);
        _rating.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _rating.BackColor = Theme.Bg;
        _rating.ForeColor = Theme.Text;
        _rating.Font = Theme.TitleFont;
        _rating.FlatStyle = FlatStyle.Flat;
        ratingCard.Controls.Add(_rating);
        _stack.Controls.Add(ratingCard);

        _scrollPanel.Controls.Add(_stack);

        Controls.Add(_scrollPanel);
        Controls.Add(footer);
        Controls.Add(statsPanel);
        Controls.Add(header);

        _scrollPanel.ClientSizeChanged += (s, e) => AdjustWidths();
        Shown += (s, e) => { AdjustWidths(); RunAutoChecks(); UpdateStats(); };
    }

    private void AdjustWidths()
    {
        if (_scrollPanel == null || _stack == null) return;
        int avail = _scrollPanel.ClientSize.Width - _scrollPanel.Padding.Horizontal;
        if (avail < 600) avail = 600;  // sane floor
        _stack.SuspendLayout();
        _stack.Width = avail;
        foreach (var c in _adaptiveControls)
        {
            c.Width = avail;
            // resize inner grid (first TableLayoutPanel child) to match
            foreach (Control child in c.Controls)
                if (child is TableLayoutPanel || child is TextBox || child is ComboBox)
                    child.Width = avail - 48;
        }
        _testsFlow.MaximumSize = new Size(avail, 0);
        _testsFlow.MinimumSize = new Size(avail, 0);
        _stack.ResumeLayout();
    }

    private static Label MakeSectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextMuted,
        Font = Theme.SectionFont,
        Margin = new Padding(4, 18, 0, 8),
    };

    private static Panel MakeCard(int width, int height)
    {
        var c = new Panel { Width = width, Height = height, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, 12) };
        c.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
            using var path = Shape.Rounded(r, 10);
            using var fill = new SolidBrush(Theme.Card);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        };
        return c;
    }

    private static Panel MakeStatTile(string label, Label valueLbl, Color valueColor)
    {
        var tile = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2, RowCount = 1,
            BackColor = Color.Transparent,
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var lbl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextMuted,
            Font = new Font("Segoe UI Variable", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
        };
        valueLbl.Font = new Font("Segoe UI Variable Display", 22, FontStyle.Bold);
        valueLbl.ForeColor = valueColor;
        valueLbl.Dock = DockStyle.Fill;
        valueLbl.AutoSize = false;
        valueLbl.TextAlign = ContentAlignment.MiddleLeft;
        valueLbl.Padding = new Padding(0, 0, 0, 0);

        inner.Controls.Add(lbl, 0, 0);
        inner.Controls.Add(valueLbl, 1, 0);
        tile.Controls.Add(inner);
        return tile;
    }

    private static void AddField(TableLayoutPanel grid, string label, Control ctrl, int row, int col)
    {
        var lbl = new Label { Text = label, AutoSize = true, ForeColor = Theme.TextMuted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 4) };
        ctrl.Dock = DockStyle.Fill;
        ctrl.Margin = new Padding(0, 4, 16, 4);
        grid.Controls.Add(lbl, col, row);
        grid.Controls.Add(ctrl, col + 1, row);
    }

    private void BuildTests()
    {
        // ---- IDENTIFICATION (auto) ----
        Add("Identification", "Computer model", null, () => { var (t, ok) = SystemInfo.ModelInfo(); _model.Text = t; return (t, ok ? TestState.Info : TestState.Fail); });
        Add("Identification", "BIOS / Service Tag", null, () => { var (t, ok) = SystemInfo.BiosInfo(); var st = t.Split("Tag:").Length > 1 ? t.Split("Tag:")[1].Trim() : ""; _serviceTag.Text = st; return (t, ok ? TestState.Info : TestState.Fail); });
        Add("Identification", "CPU", null, () => { var (t, ok) = SystemInfo.CpuInfo(); return (t, ok ? TestState.Pass : TestState.Fail); });
        Add("Identification", "RAM", null, () => { var (t, ok) = SystemInfo.RamInfo(); return (t, ok ? TestState.Pass : TestState.Fail); });
        Add("Identification", "Storage", null, () => { var (t, ok) = SystemInfo.DiskInfo(); return (t, ok ? TestState.Pass : TestState.Fail); });
        Add("Identification", "TPM", null, () => { var (t, ok) = SystemInfo.TpmInfo(); return (t, ok ? TestState.Pass : TestState.Fail); });

        // ---- DISK HEALTH ----
        Add("Disk health", "SMART disk health", it =>
        {
            var reports = SmartTest.Run();
            using var f = new SmartReportForm(reports);
            f.ShowDialog(this);
            bool anyFail = reports.Any(r => r.PredictFailure || (r.Verdict != "OK" && !r.Verdict.StartsWith("Data")));
            it.State = anyFail ? TestState.Fail : reports.Count > 0 ? TestState.Pass : TestState.Fail;
            it.Detail = reports.Count == 0
                ? "SMART not available"
                : string.Join("; ", reports.Select(r => $"{r.Verdict}"));
            it.Card?.Refresh(); UpdateStats();
        }, () =>
        {
            var reports = SmartTest.Run();
            if (reports.Count == 0) return ("SMART not available", TestState.Fail);
            bool fail = reports.Any(r => r.PredictFailure);
            return (string.Join(" | ", reports.Select(r => $"{(r.PredictFailure ? "⚠ " : "")}{r.Verdict}")), fail ? TestState.Fail : TestState.Pass);
        });
        Add("Disk health", "Disk benchmark (256 MB write/read)", it =>
        {
            it.State = TestState.Running; it.Detail = "Benchmark running…"; it.Card?.Refresh();
            Application.DoEvents();
            try
            {
                var (w, r, log) = DiskBenchmark.Run();
                it.State = (w >= 50 && r >= 100) ? TestState.Pass : (r >= 50 ? TestState.Pass : TestState.Fail);
                it.Detail = log;
            }
            catch (Exception ex) { it.State = TestState.Fail; it.Detail = "Error: " + ex.Message; }
            it.Card?.Refresh(); UpdateStats();
        }, null);
        Add("Disk health", "Full disk surface scan (whole drive)", it =>
        {
            using var f = new FullDiskBenchmarkForm();
            f.ShowDialog(this);
            it.State = f.PassedFlag ? TestState.Pass : TestState.Fail;
            it.Detail = f.ResultText;
            it.Card?.Refresh(); UpdateStats();
        }, null);

        // ---- MEMORY ----
        Add("Memory", "Memory pattern test (50% free RAM)", it =>
        {
            using var f = new MemoryTestForm();
            f.ShowDialog(this);
            it.State = f.PassedFlag ? TestState.Pass : TestState.Fail;
            it.Detail = f.ResultText;
            it.Card?.Refresh(); UpdateStats();
        }, null);

        // ---- BATTERY (auto) ----
        Add("Battery", "Battery health", null, () =>
        {
            var (t, ok, _) = SystemInfo.BatteryHealth();
            return (t, ok ? TestState.Pass : TestState.Fail);
        });

        // ---- THERMAL (auto) ----
        Add("Thermal", "CPU / GPU temperatures + fan", null, () =>
        {
            try
            {
                var sum = SystemInfo.SharedSensors.Summary();
                var t = SystemInfo.SharedSensors.CpuPackageTempC() ?? 0;
                return (sum, t > 0 && t < 85 ? TestState.Pass : t == 0 ? TestState.Info : TestState.Fail);
            }
            catch (Exception ex) { return ("Error: " + ex.Message, TestState.Fail); }
        });

        // ---- DISPLAY ----
        Add("Display", "Display detection", null, () => { var (t, ok) = SystemInfo.DisplayInfo(); return (t, ok ? TestState.Info : TestState.Fail); });
        Add("Display", "Dead pixel + backlight bleeding", it =>
        {
            using var f = new DisplayTestForm();
            f.ShowDialog(this);
            it.Detail = "Manually checked via fullscreen colors (black, white, R/G/B, gray)";
            it.State = TestState.Pass;
            it.Card?.Refresh();
            UpdateStats();
        }, null);

        // ---- KEYBOARD ----
        Add("Keyboard", "Keyboard key test", it =>
        {
            using var f = new KeyboardTestForm();
            f.ShowDialog(this);
            it.State = f.PassedFlag ? TestState.Pass : TestState.Fail;
            it.Detail = f.PassedFlag ? "Keyboard OK" : "Marked as faulty";
            it.Card?.Refresh();
            UpdateStats();
        }, null);

        // ---- TOUCHPAD ----
        Add("Touchpad", "Movement, clicks, scroll", it =>
        {
            using var f = new TouchpadTestForm();
            f.ShowDialog(this);
            it.State = f.PassedFlag ? TestState.Pass : TestState.Fail;
            it.Detail = f.PassedFlag ? "Touchpad OK" : "Marked as faulty";
            it.Card?.Refresh();
            UpdateStats();
        }, null);

        // ---- AUDIO ----
        Add("Audio", "Speaker LEFT — 1 kHz tone", it => RunAudio(it, 0, "left"), null);
        Add("Audio", "Speaker RIGHT — 1 kHz tone", it => RunAudio(it, 1, "right"), null);
        Add("Audio", "Stereo / 3.5mm headphones", it => RunAudio(it, 2, "stereo"), null);
        Add("Audio", "Microphone — 3s record and playback", it =>
        {
            it.State = TestState.Running; it.Detail = "Recording 3 seconds…"; it.Card?.Refresh();
            Application.DoEvents();
            var result = MicTest.RecordAndPlay(3);
            var ok = result.StartsWith("Recording");
            if (ok && MessageBox.Show(result + "\n\nDid you hear the playback?", "Microphone", MessageBoxButtons.YesNo) == DialogResult.Yes)
            { it.State = TestState.Pass; it.Detail = "Microphone and playback OK"; }
            else { it.State = TestState.Fail; it.Detail = result; }
            it.Card?.Refresh(); UpdateStats();
        }, null);

        // ---- WEBCAM ----
        Add("Webcam", "Webcam live preview", it =>
        {
            using var f = new WebcamTestForm();
            f.ShowDialog(this);
            it.State = f.PassedFlag ? TestState.Pass : TestState.Fail;
            it.Detail = f.PassedFlag ? "Camera image OK" : "No image / fault / lens cover";
            it.Card?.Refresh(); UpdateStats();
        }, null);

        // ---- USB ----
        Add("USB", "Sequential USB port test", it =>
        {
            using var f = new UsbTestForm();
            f.ShowDialog(this);
            it.State = f.PassedFlag ? TestState.Pass : TestState.Fail;
            it.Detail = $"Detected {f.DetectedCount} connections";
            it.Card?.Refresh(); UpdateStats();
        }, null);

        // ---- STRESS ----
        Add("Stress", "CPU stress 2 min + temp monitoring", it =>
        {
            using var f = new StressTestForm(120);
            f.ShowDialog(this);
            it.State = f.PassedFlag ? TestState.Pass : TestState.Fail;
            it.Detail = $"Max temperature reached: {f.MaxTempReached:F1} °C  (limit 95 °C)";
            it.Card?.Refresh(); UpdateStats();
        }, null);
        Add("Stress", "CPU stress 5 min (longer load)", it =>
        {
            using var f = new StressTestForm(300);
            f.ShowDialog(this);
            it.State = f.PassedFlag ? TestState.Pass : TestState.Fail;
            it.Detail = $"Max temperature reached: {f.MaxTempReached:F1} °C  (limit 95 °C)";
            it.Card?.Refresh(); UpdateStats();
        }, null);
        Add("Stress", "Dell QuickTest (online)", it =>
        {
            ShellLaunch.DellQuickTest(_serviceTag.Text);
            if (MessageBox.Show("Dell QuickTest opened in browser. Did the diagnostics pass without errors?", "Dell QuickTest", MessageBoxButtons.YesNo) == DialogResult.Yes)
            { it.State = TestState.Pass; it.Detail = "Dell QuickTest passed"; }
            else { it.State = TestState.Fail; it.Detail = "Dell QuickTest reported issue"; }
            it.Card?.Refresh(); UpdateStats();
        }, null);

        // ---- NETWORK ----
        Add("Network", "Wi-Fi adapter", null, () => { var (t, ok) = SystemInfo.NetworkInfo(NetworkInterfaceType.Wireless80211); return (t, ok ? TestState.Pass : TestState.Fail); });
        Add("Network", "Ethernet (RJ-45)", null, () => { var (t, ok) = SystemInfo.NetworkInfo(NetworkInterfaceType.Ethernet); return (t, ok ? TestState.Pass : TestState.Fail); });
        Add("Network", "Bluetooth adapter", null, () => { var (t, ok) = SystemInfo.BluetoothInfo(); return (t, ok ? TestState.Pass : TestState.Fail); });
        Add("Network", "Ping test (gateway, DNS, internet)", it =>
        {
            it.State = TestState.Running; it.Detail = "Pinging…"; it.Card?.Refresh();
            Application.DoEvents();
            var (ok, txt) = NetworkPing.Run();
            it.State = ok ? TestState.Pass : TestState.Fail;
            it.Detail = txt;
            it.Card?.Refresh(); UpdateStats();
        }, null);
    }

    private void RunAudio(TestItem it, int ch, string name)
    {
        it.State = TestState.Running; it.Detail = $"Playing tone to {name} channel…"; it.Card?.Refresh();
        Application.DoEvents();
        try { AudioTest.PlayTone(ch); }
        catch (Exception ex) { it.State = TestState.Fail; it.Detail = "Error: " + ex.Message; it.Card?.Refresh(); return; }
        if (MessageBox.Show($"Did you hear the tone in the {name} channel?", "Audio test", MessageBoxButtons.YesNo) == DialogResult.Yes)
        { it.State = TestState.Pass; it.Detail = $"Tone in {name} channel OK"; }
        else { it.State = TestState.Fail; it.Detail = $"No tone in {name} channel"; }
        it.Card?.Refresh(); UpdateStats();
    }

    private void Add(string cat, string title, Action<TestItem> action, Func<(string, TestState)> autoCheck)
    {
        var t = new TestItem { Category = cat, Title = title, Action = action, AutoCheck = autoCheck, Detail = "—" };
        _tests.Add(t);
    }

    private void RunAutoChecks()
    {
        foreach (var t in _tests)
        {
            if (t.AutoCheck == null) continue;
            try
            {
                var (detail, st) = t.AutoCheck();
                t.Detail = detail;
                t.State = st;
            }
            catch (Exception ex) { t.Detail = "Chyba: " + ex.Message; t.State = TestState.Fail; }
            t.Card?.Refresh();
        }
        UpdateStats();
    }

    public void RefreshStats() => UpdateStats();

    private void UpdateStats()
    {
        int total = _tests.Count;
        int pass = _tests.Count(t => t.State == TestState.Pass);
        int fail = _tests.Count(t => t.State == TestState.Fail);
        int untested = _tests.Count(t => t.State == TestState.Untested);
        _statTotal.Text = total.ToString();
        _statPass.Text = pass.ToString();
        _statFail.Text = fail.ToString();
        _statUntested.Text = untested.ToString();
    }

    private LaptopResult BuildResult()
    {
        var failed = _tests.Where(t => t.State == TestState.Fail).Select(t => t.Title).ToList();
        var visualUnchecked = _visualChecks.Where(v => !v.Box.Checked).Select(v => v.Label).ToList();
        return new LaptopResult
        {
            ServiceTag = _serviceTag.Text.Trim(),
            Model = _model.Text.Trim(),
            Status = _rating.SelectedItem?.ToString() ?? "",
            Notes = _notes.Text.Trim(),
            Tester = _tester.Text.Trim(),
            Cpu = ExtractCpu(TestDetail("CPU")),
            Ram = ExtractRam(TestDetail("RAM")),
            Storage = ExtractStorage(TestDetail("Storage")),
            TestedAt = _date.Value,
            TestsPass = _tests.Count(t => t.State == TestState.Pass),
            TestsFail = _tests.Count(t => t.State == TestState.Fail),
            TestsTotal = _tests.Count,
            Failed = failed,
            VisualUnchecked = visualUnchecked,
        };
    }

    private string TestDetail(string title) =>
        _tests.FirstOrDefault(t => t.Title == title)?.Detail ?? "";

    // "12th Gen Intel(R) Core(TM) i7-1265U  10 cores..." -> "i7-1265U"
    private static string ExtractCpu(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "";
        var m = System.Text.RegularExpressions.Regex.Match(detail, @"i[3579]-\w+|Ryzen\s+\d+\s+\w+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Value : detail.Split('(', '/')[0].Trim();
    }

    // "Total 16 GB (...)" -> "16 GB"
    private static string ExtractRam(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "";
        var m = System.Text.RegularExpressions.Regex.Match(detail, @"\d+\s*GB");
        return m.Success ? m.Value : "";
    }

    // "CT500P5PSSD8 • 465 GB • SCSI" -> "465 GB SSD"  (first disk)
    private static string ExtractStorage(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "";
        var firstLine = detail.Split('\n')[0];
        var m = System.Text.RegularExpressions.Regex.Match(firstLine, @"\d+(\.\d+)?\s*TB|\d+\s*GB");
        var size = m.Success ? m.Value : "";
        var kind = (firstLine.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    firstLine.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0) ? " SSD" : "";
        return string.IsNullOrEmpty(size) ? "" : (size + kind);
    }

    private void SaveJsonResult()
    {
        var r = BuildResult();
        if (string.IsNullOrEmpty(r.ServiceTag))
        {
            MessageBox.Show("Service Tag is empty — fill it in the header or let autodetection finish.", "Missing Service Tag", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrEmpty(r.Status))
        {
            if (MessageBox.Show("Overall condition is not selected at the bottom.\n\nSave anyway (JSON will have empty value)?", "No condition", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
        }
        try
        {
            var path = ResultStore.Save(r);
            MessageBox.Show($"Saved:\n{path}\n\nKeep this folder (results/) next to the app, e.g. on a USB stick. On your main PC click \"Batch import…\" and select this folder.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show("Save error: " + ex.Message); }
    }

    private void ExportToExcel()
    {
        var st = _serviceTag.Text?.Trim();
        if (string.IsNullOrEmpty(st))
        {
            MessageBox.Show("Service Tag is empty — fill it in the header or let autodetection finish.", "Missing Service Tag", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_rating.SelectedItem == null)
        {
            MessageBox.Show("Select Overall condition at the bottom — this value will be written to Excel.", "Select condition", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new OpenFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            Title = "Select Excel inventory file",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        // If the tag isn't in the file, offer to add a new row.
        var addNew = MessageBox.Show(
            "If this Service Tag is not yet in the inventory, add it as a NEW row?\n\n" +
            "Yes = add new row (copies price formulas from the last row)\n" +
            "No  = only update if it already exists",
            "Add new row?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        var res = BuildResult();
        var (ok, msg) = ExcelExport.UpdateOrAppend(dlg.FileName, res, allowAppend: addNew);
        if (ok)
            MessageBox.Show($"Done.\n\n{msg}\n\nService Tag: {st}\nCondition: {res.Status}\nSpec: {res.Spec}", "Excel updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
            MessageBox.Show($"Update failed:\n\n{msg}\n\nPossible causes:\n• Service Tag '{st}' not in Excel (and adding was declined)\n• File open in Excel (close it)\n• Header columns named differently than 'Service Tag' / 'Stav'", "Excel — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void Export()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Text report (*.txt)|*.txt|CSV (*.csv)|*.csv",
            FileName = $"Report_{(_serviceTag.Text.Length > 0 ? _serviceTag.Text : "laptop")}_{DateTime.Now:yyyyMMdd}.txt"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        bool csv = dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        if (csv)
        {
            sb.AppendLine("Category;Test;State;Detail");
            foreach (var t in _tests)
                sb.AppendLine($"\"{t.Category}\";\"{t.Title}\";{t.State};\"{t.Detail.Replace("\"", "''")}\"");
        }
        else
        {
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("  LAPTOP DIAGNOSTIC REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"  Model:         {_model.Text}");
            sb.AppendLine($"  Service Tag:   {_serviceTag.Text}");
            sb.AppendLine($"  Date:          {_date.Value:dd.MM.yyyy}");
            sb.AppendLine();

            int total = _tests.Count, pass = _tests.Count(t => t.State == TestState.Pass), fail = _tests.Count(t => t.State == TestState.Fail);
            sb.AppendLine($"  RESULT: {pass}/{total} pass, {fail} fail");
            sb.AppendLine();

            string lastCat = null;
            foreach (var t in _tests)
            {
                if (t.Category != lastCat)
                {
                    sb.AppendLine();
                    sb.AppendLine($"── {t.Category.ToUpper()} ──");
                    lastCat = t.Category;
                }
                string mark = t.State switch
                {
                    TestState.Pass => "[PASS]",
                    TestState.Fail => "[FAIL]",
                    TestState.Info => "[INFO]",
                    _ => "[  - ]"
                };
                sb.AppendLine($"  {mark}  {t.Title}");
                if (!string.IsNullOrWhiteSpace(t.Detail) && t.Detail != "—")
                    foreach (var line in t.Detail.Split('\n'))
                        sb.AppendLine($"         {line.Trim()}");
            }
            sb.AppendLine();
            sb.AppendLine("── VISUAL INSPECTION ──");
            foreach (var (label, box) in _visualChecks)
                sb.AppendLine($"  [{(box.Checked ? "OK " : "   ")}]  {label}");

            sb.AppendLine();
            sb.AppendLine("── NOTES ──");
            sb.AppendLine(_notes.Text);

            sb.AppendLine();
            sb.AppendLine("── OVERALL CONDITION ──");
            sb.AppendLine($"  {_rating.SelectedItem ?? "(not selected)"}");
        }

        File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
        MessageBox.Show("Report saved:\n" + dlg.FileName, "Done");
    }
}
