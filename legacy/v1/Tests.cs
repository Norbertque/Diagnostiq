using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KontrolniProtokol;

// =====================================================================
// THEME — light, modern
// =====================================================================
public static class Theme
{
    public static readonly Color Bg          = Color.FromArgb(241, 245, 249); // slate-100
    public static readonly Color Card        = Color.FromArgb(255, 255, 255); // white
    public static readonly Color CardHover   = Color.FromArgb(248, 250, 252); // slate-50
    public static readonly Color Border      = Color.FromArgb(226, 232, 240); // slate-200
    public static readonly Color BorderStrong= Color.FromArgb(203, 213, 225); // slate-300
    public static readonly Color Text        = Color.FromArgb(15, 23, 42);    // slate-900
    public static readonly Color TextMuted   = Color.FromArgb(100, 116, 139); // slate-500
    public static readonly Color TextDim     = Color.FromArgb(148, 163, 184); // slate-400
    public static readonly Color Accent      = Color.FromArgb(37, 99, 235);   // blue-600
    public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);   // blue-700
    public static readonly Color Success     = Color.FromArgb(22, 163, 74);   // green-600
    public static readonly Color SuccessHover= Color.FromArgb(21, 128, 61);   // green-700
    public static readonly Color SuccessSoft = Color.FromArgb(220, 252, 231); // green-100
    public static readonly Color Danger      = Color.FromArgb(220, 38, 38);   // red-600
    public static readonly Color DangerHover = Color.FromArgb(185, 28, 28);   // red-700
    public static readonly Color DangerSoft  = Color.FromArgb(254, 226, 226); // red-100
    public static readonly Color Warning     = Color.FromArgb(234, 88, 12);   // orange-600
    public static readonly Color WarningSoft = Color.FromArgb(254, 215, 170); // orange-200
    public static readonly Color Untested    = Color.FromArgb(148, 163, 184); // slate-400
    public static readonly Color UntestedSoft= Color.FromArgb(241, 245, 249);

    public static readonly Font HeaderFont   = new("Segoe UI Variable Display", 22, FontStyle.Bold);
    public static readonly Font SectionFont  = new("Segoe UI Variable", 10, FontStyle.Bold);
    public static readonly Font TitleFont    = new("Segoe UI Variable", 11, FontStyle.Bold);
    public static readonly Font BodyFont     = new("Segoe UI Variable", 9);
    public static readonly Font SmallFont    = new("Segoe UI Variable", 8);
    public static readonly Font MonoFont     = new("Consolas", 9);
    public static readonly Font PillFont     = new("Segoe UI Variable", 8, FontStyle.Bold);
    public static readonly Font ButtonFont   = new("Segoe UI Variable", 10, FontStyle.Bold);
}

// Common helpers for rounded shapes
public static class Shape
{
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        int d = radius * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// =====================================================================
// DISPLAY TEST — fullscreen color cycler
// =====================================================================
public class DisplayTestForm : Form
{
    private readonly Color[] _colors = { Color.Black, Color.White, Color.Red, Color.Green, Color.Blue, Color.FromArgb(128, 128, 128) };
    private readonly string[] _names = { "BLACK — dead pixels, backlight bleeding", "WHITE — dead pixels, stains", "RED", "GREEN", "BLUE", "GRAY — uniformity" };
    private int _idx;
    private readonly Label _info;

    public DisplayTestForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        BackColor = _colors[0];
        Cursor = Cursors.Hand;

        _info = new Label
        {
            Text = Hint(),
            AutoSize = true,
            ForeColor = Color.Yellow,
            BackColor = Color.FromArgb(160, 0, 0, 0),
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Padding = new Padding(14, 10, 14, 10),
            Location = new Point(24, 24)
        };
        Controls.Add(_info);

        Click += (s, e) => Next();
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Right) Next();
            else if (e.KeyCode == Keys.Left) Prev();
            else if (e.KeyCode == Keys.H) _info.Visible = !_info.Visible;
        };
        KeyPreview = true;
    }

    private string Hint() => $"{_names[_idx]}   [{_idx + 1}/{_colors.Length}]\n" +
                              "Click / Space / →  next    ←  back    H  hide text    ESC  exit";

    private void Next() { _idx = (_idx + 1) % _colors.Length; Apply(); }
    private void Prev() { _idx = (_idx - 1 + _colors.Length) % _colors.Length; Apply(); }
    private void Apply()
    {
        BackColor = _colors[_idx];
        _info.Text = Hint();
        var bright = (BackColor.R + BackColor.G + BackColor.B) > 400;
        _info.ForeColor = bright ? Color.Black : Color.Yellow;
    }
}

// =====================================================================
// KEYBOARD TEST
// =====================================================================
public class KeyboardTestForm : Form
{
    private readonly Label _key = new() { Font = new Font("Consolas", 36, FontStyle.Bold), AutoSize = true, ForeColor = Theme.Accent };
    private readonly TextBox _log = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, ReadOnly = true, Font = Theme.MonoFont, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
    private readonly HashSet<Keys> _seen = new();
    private readonly Label _count = new() { AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.BodyFont };

    public bool PassedFlag { get; private set; }

    public KeyboardTestForm()
    {
        Text = "Keyboard test";
        Width = 760; Height = 520;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterParent;

        var top = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Theme.Bg, Padding = new Padding(24, 16, 24, 8) };
        _key.Location = new Point(24, 16);
        _count.Location = new Point(24, 76);
        top.Controls.Add(_key); top.Controls.Add(_count);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg, Padding = new Padding(24, 10, 24, 10) };
        var btnPass = ModernButton.Make("Keyboard OK", Theme.Success);
        btnPass.Location = new Point(24, 10); btnPass.Width = 160;
        btnPass.Click += (s, e) => { PassedFlag = true; DialogResult = DialogResult.OK; Close(); };

        var btnFail = ModernButton.Make("Mark fault", Theme.Danger);
        btnFail.Location = new Point(196, 10); btnFail.Width = 160;
        btnFail.Click += (s, e) => { PassedFlag = false; DialogResult = DialogResult.OK; Close(); };

        var btnReset = ModernButton.Make("Reset", Theme.CardHover);
        btnReset.Location = new Point(368, 10); btnReset.Width = 100;
        btnReset.Click += (s, e) => { _seen.Clear(); _log.Clear(); UpdateCount(); };

        bottom.Controls.Add(btnPass); bottom.Controls.Add(btnFail); bottom.Controls.Add(btnReset);

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(24, 12, 24, 12) };
        center.Controls.Add(_log);

        Controls.Add(center);
        Controls.Add(top);
        Controls.Add(bottom);

        KeyDown += (s, e) =>
        {
            _key.Text = e.KeyCode.ToString() + (e.Modifiers != Keys.None ? "  +" + e.Modifiers : "");
            var isNew = _seen.Add(e.KeyCode);
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {(isNew ? "NEW " : "    ")} {e.KeyCode} (code={(int)e.KeyCode})\r\n");
            UpdateCount();
            e.SuppressKeyPress = true;
        };
        UpdateCount();
    }
    private void UpdateCount() => _count.Text = $"Unique keys pressed: {_seen.Count}";
}

// =====================================================================
// TOUCHPAD TEST — drawing canvas, click counter, scroll detection
// =====================================================================
public class TouchpadTestForm : Form
{
    private readonly List<Point> _trail = new();
    private int _leftClicks, _rightClicks, _scrollEvents;
    private readonly Label _status = new() { AutoSize = true, ForeColor = Theme.Text, Font = Theme.BodyFont };
    private readonly Panel _canvas = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    public bool PassedFlag { get; private set; }

    public TouchpadTestForm()
    {
        Text = "Touchpad test";
        Width = 820; Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;

        var top = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Theme.Bg, Padding = new Padding(24, 16, 24, 8) };
        var title = new Label { Text = "Move the cursor over the whole area, left/right click, try two-finger scroll.", AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.BodyFont, Location = new Point(24, 16) };
        _status.Location = new Point(24, 44);
        top.Controls.Add(title); top.Controls.Add(_status);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg, Padding = new Padding(24, 10, 24, 10) };
        var btnPass = ModernButton.Make("Touchpad OK", Theme.Success);
        btnPass.Location = new Point(24, 10); btnPass.Width = 160;
        btnPass.Click += (s, e) => { PassedFlag = true; DialogResult = DialogResult.OK; Close(); };
        var btnFail = ModernButton.Make("Mark fault", Theme.Danger);
        btnFail.Location = new Point(196, 10); btnFail.Width = 160;
        btnFail.Click += (s, e) => { PassedFlag = false; DialogResult = DialogResult.OK; Close(); };
        var btnClear = ModernButton.Make("Clear", Theme.CardHover);
        btnClear.Location = new Point(368, 10); btnClear.Width = 100;
        btnClear.Click += (s, e) => { _trail.Clear(); _leftClicks = _rightClicks = _scrollEvents = 0; _canvas.Invalidate(); UpdateStatus(); };
        bottom.Controls.Add(btnPass); bottom.Controls.Add(btnFail); bottom.Controls.Add(btnClear);

        _canvas.MouseMove += (s, e) =>
        {
            _trail.Add(e.Location);
            if (_trail.Count > 5000) _trail.RemoveRange(0, 1000);
            _canvas.Invalidate();
        };
        _canvas.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) _leftClicks++;
            else if (e.Button == MouseButtons.Right) _rightClicks++;
            UpdateStatus();
        };
        _canvas.MouseWheel += (s, e) => { _scrollEvents++; UpdateStatus(); };
        _canvas.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Accent, 2);
            for (int i = 1; i < _trail.Count; i++)
            {
                if (Math.Abs(_trail[i].X - _trail[i - 1].X) < 80 && Math.Abs(_trail[i].Y - _trail[i - 1].Y) < 80)
                    g.DrawLine(pen, _trail[i - 1], _trail[i]);
            }
        };

        Controls.Add(_canvas);
        Controls.Add(top);
        Controls.Add(bottom);
        UpdateStatus();
    }

    private void UpdateStatus() => _status.Text = $"Left: {_leftClicks}     Right: {_rightClicks}     Scroll: {_scrollEvents}     Trail points: {_trail.Count}";
}

// =====================================================================
// AUDIO PLAYBACK — generate stereo WAV with tone on L/R/both
// =====================================================================
public static class AudioTest
{
    public static void PlayTone(int channel, int freq = 1000, double seconds = 1.5)
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * seconds);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int byteRate = sampleRate * 2 * 2;
        int dataSize = samples * 4;

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)2);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)4);
        bw.Write((short)16);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / sampleRate;
            double env = 1.0;
            if (t < 0.05) env = t / 0.05;
            else if (t > seconds - 0.05) env = (seconds - t) / 0.05;
            short val = (short)(Math.Sin(2 * Math.PI * freq * t) * 0.5 * 32767 * env);
            short left = (channel == 0 || channel == 2) ? val : (short)0;
            short right = (channel == 1 || channel == 2) ? val : (short)0;
            bw.Write(left);
            bw.Write(right);
        }
        bw.Flush();
        ms.Position = 0;
        using var player = new SoundPlayer(ms);
        player.PlaySync();
    }
}

// =====================================================================
// MICROPHONE TEST — record via MCI, play back
// =====================================================================
public static class MicTest
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder buffer, int bufferSize, IntPtr callback);

    public static string RecordAndPlay(int seconds = 3)
    {
        string path = Path.Combine(Path.GetTempPath(), "kp_mic_test.wav");
        if (File.Exists(path)) try { File.Delete(path); } catch { }
        var sb = new StringBuilder(256);
        int r;
        r = mciSendString("open new type waveaudio alias capture", sb, 256, IntPtr.Zero);
        if (r != 0) return "Cannot open input device (code " + r + ")";
        mciSendString("set capture bitspersample 16 channels 1 samplespersec 44100", sb, 256, IntPtr.Zero);
        mciSendString("record capture", sb, 256, IntPtr.Zero);
        Thread.Sleep(seconds * 1000);
        mciSendString("stop capture", sb, 256, IntPtr.Zero);
        mciSendString($"save capture \"{path}\"", sb, 256, IntPtr.Zero);
        mciSendString("close capture", sb, 256, IntPtr.Zero);
        if (!File.Exists(path)) return "Recording was not saved";
        using (var player = new SoundPlayer(path)) player.PlaySync();
        var len = new FileInfo(path).Length;
        try { File.Delete(path); } catch { }
        return $"Recording {seconds}s OK ({len / 1024} kB) — did you hear the playback?";
    }
}

// =====================================================================
// SYSTEM INFO via WMI
// =====================================================================
public static class SystemInfo
{
    public static (string text, bool ok) BiosInfo()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS");
            foreach (ManagementObject m in s.Get())
                return ($"{m["Manufacturer"]}  •  BIOS {m["SMBIOSBIOSVersion"]}  •  Service Tag: {m["SerialNumber"]}", true);
        }
        catch (Exception ex) { return ("Error: " + ex.Message, false); }
        return ("Not found", false);
    }

    public static (string text, bool ok) ModelInfo()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (ManagementObject m in s.Get())
                return ($"{m["Manufacturer"]} {m["Model"]}", true);
        }
        catch (Exception ex) { return ("Error: " + ex.Message, false); }
        return ("Not found", false);
    }

    public static (string text, bool ok) CpuInfo()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (ManagementObject m in s.Get())
                return ($"{m["Name"]}   {m["NumberOfCores"]} cores / {m["NumberOfLogicalProcessors"]} threads @ {m["MaxClockSpeed"]} MHz", true);
        }
        catch (Exception ex) { return ("Error: " + ex.Message, false); }
        return ("Not found", false);
    }

    public static (string text, bool ok) RamInfo()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Capacity, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
            ulong total = 0; var parts = new List<string>();
            foreach (ManagementObject m in s.Get())
            {
                ulong cap = (ulong)(m["Capacity"] ?? 0UL);
                total += cap;
                parts.Add($"{cap / (1024 * 1024 * 1024)} GB @ {m["Speed"]}MHz {((string)m["Manufacturer"])?.Trim()}");
            }
            return ($"Total {total / (1024 * 1024 * 1024)} GB   ({parts.Count} module(s): {string.Join(", ", parts)})", total > 0);
        }
        catch (Exception ex) { return ("Error: " + ex.Message, false); }
    }

    public static (string text, bool ok) DiskInfo()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Model, Size, MediaType, InterfaceType FROM Win32_DiskDrive");
            var lines = new List<string>();
            foreach (ManagementObject m in s.Get())
            {
                ulong size = (ulong)(m["Size"] ?? 0UL);
                lines.Add($"{m["Model"]} • {size / (1024UL * 1024 * 1024)} GB • {m["InterfaceType"]}");
            }
            return (string.Join("\n", lines), lines.Count > 0);
        }
        catch (Exception ex) { return ("Error: " + ex.Message, false); }
    }

    public static (string text, bool ok, double healthPct) BatteryHealth()
    {
        try
        {
            uint design = 0, full = 0;
            try
            {
                using var w = new ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData");
                foreach (ManagementObject m in w.Get()) design = (uint)m["DesignedCapacity"];
                using var f = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
                foreach (ManagementObject m in f.Get()) full = (uint)m["FullChargedCapacity"];
            }
            catch { }

            string current = "";
            try
            {
                using var s = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
                foreach (ManagementObject m in s.Get())
                    current = $"   Current charge: {m["EstimatedChargeRemaining"]}%";
            }
            catch { }

            var ps = SystemInformation.PowerStatus;
            var adapter = ps.PowerLineStatus == PowerLineStatus.Online ? "connected" : "disconnected";

            if (design > 0 && full > 0)
            {
                double health = 100.0 * full / design;
                string rating = health >= 80 ? "Excellent" : health >= 60 ? "Good" : health >= 40 ? "Fair" : "Poor";
                return ($"Health: {health:F1}%  ({rating})   Design: {design} mWh   Full: {full} mWh{current}   Adapter: {adapter}", health >= 60, health);
            }
            return ($"Cannot read design capacity{current}   Adapter: {adapter}", false, 0);
        }
        catch (Exception ex) { return ("Error: " + ex.Message, false, 0); }
    }

    public static (string text, bool ok) TpmInfo()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftTpm", "SELECT * FROM Win32_Tpm");
            foreach (ManagementObject m in s.Get())
            {
                var enabled = (bool)(m["IsEnabled_InitialValue"] ?? false);
                var activated = (bool)(m["IsActivated_InitialValue"] ?? false);
                return ($"Spec {m["SpecVersion"]}   Enabled: {enabled}   Active: {activated}", enabled && activated);
            }
            return ("TPM not found", false);
        }
        catch (Exception ex) { return ("Error (try running as admin): " + ex.Message, false); }
    }

    private static Sensors _sharedSensors;
    public static Sensors SharedSensors => _sharedSensors ??= new Sensors();

    public static (string text, bool ok) ThermalInfo()
    {
        // Try LibreHardwareMonitor first (accurate)
        try
        {
            SharedSensors.Update();
            var cpu = SharedSensors.CpuPackageTempC();
            var gpu = SharedSensors.GpuTempC();
            var fans = SharedSensors.Fans();
            if (cpu.HasValue || gpu.HasValue || fans.Count > 0)
            {
                var parts = new List<string>();
                if (cpu.HasValue) parts.Add($"CPU {cpu.Value:F1} °C");
                if (gpu.HasValue) parts.Add($"GPU {gpu.Value:F1} °C");
                if (fans.Count > 0) parts.Add(string.Join(" / ", fans.Select(f => $"{f.name} {f.rpm} RPM")));
                double maxC = Math.Max(cpu ?? 0, gpu ?? 0);
                var max = maxC > 0 ? $"Max: {maxC:F1} °C   " : "";
                return ($"{max}{string.Join("   ", parts)}", maxC > 0 && maxC < 85);
            }
        }
        catch { }
        // Fallback to WMI ACPI thermal zones
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature, InstanceName FROM MSAcpi_ThermalZoneTemperature");
            var lines = new List<string>();
            double maxC = 0;
            foreach (ManagementObject m in s.Get())
            {
                double tempC = ((uint)m["CurrentTemperature"] / 10.0) - 273.15;
                if (tempC > maxC) maxC = tempC;
                lines.Add($"{tempC:F1} °C");
            }
            if (lines.Count == 0) return ("No thermal sensors available", false);
            return ($"Max: {maxC:F1} °C   ACPI: {string.Join(", ", lines)}", maxC < 85);
        }
        catch (Exception ex) { return ("Error: " + ex.Message, false); }
    }

    public static (string text, bool ok) NetworkInfo(NetworkInterfaceType type)
    {
        try
        {
            var matches = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.NetworkInterfaceType == type)
                .ToList();
            if (matches.Count == 0) return ("Adapter not found", false);

            // Prefer an Up adapter with assigned IPv4
            NetworkInterface best = null;
            foreach (var ni in matches)
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var props = ni.GetIPProperties();
                var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ipv4 != null && !ipv4.Address.ToString().StartsWith("169.254"))   // skip APIPA
                { best = ni; break; }
                if (best == null) best = ni;  // fallback: any Up
            }
            if (best == null)
            {
                // None Up — show first with its status
                var first = matches[0];
                return ($"{first.Name}   not connected ({first.OperationalStatus})", false);
            }

            var p = best.GetIPProperties();
            var ip = p.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? "—";
            var gw = p.GatewayAddresses.FirstOrDefault(g => g.Address != null && g.Address.ToString() != "0.0.0.0")?.Address.ToString() ?? "—";
            var speed = best.Speed > 0 ? $"{best.Speed / 1_000_000} Mb/s" : "—";

            string extra = "";
            if (type == NetworkInterfaceType.Wireless80211)
            {
                var ssid = GetWifiSsid();
                if (!string.IsNullOrEmpty(ssid)) extra = $"SSID: {ssid}   ";
            }

            return ($"{extra}{best.Name}   IP: {ip}   GW: {gw}   {speed}", true);
        }
        catch (Exception ex) { return ("Error: " + ex.Message, false); }
    }

    public static string GetWifiSsid()
    {
        try
        {
            var p = new System.Diagnostics.Process();
            p.StartInfo.FileName = "netsh";
            p.StartInfo.Arguments = "wlan show interfaces";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                // Match "SSID : ..." but NOT "BSSID : ..."
                if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = line.IndexOf(':');
                    if (idx > 0)
                    {
                        var ssid = line.Substring(idx + 1).Trim();
                        if (!string.IsNullOrWhiteSpace(ssid)) return ssid;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public static (string text, bool ok) BluetoothInfo()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name, Status FROM Win32_PnPEntity WHERE Service='BTHUSB' OR Service='BthLEEnum' OR Name LIKE '%Bluetooth%Radio%'");
            foreach (ManagementObject m in s.Get())
                return ($"{m["Name"]}   {m["Status"]}", ((string)m["Status"] == "OK"));
            return ("Bluetooth adapter not found", false);
        }
        catch (Exception ex) { return ("Error: " + ex.Message, false); }
    }

    public static (string text, bool ok) DisplayInfo()
    {
        var n = Screen.AllScreens.Length;
        var parts = Screen.AllScreens.Select(sc => $"{sc.Bounds.Width}×{sc.Bounds.Height}{(sc.Primary ? " (primary)" : "")}");
        return ($"{n} {(n == 1 ? "display" : "displays")} connected: {string.Join(", ", parts)}", n >= 1);
    }

    public static List<string> ListRemovable()
    {
        var list = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
            if (d.DriveType == DriveType.Removable)
                try { list.Add($"{d.Name}  {d.VolumeLabel}  {d.DriveFormat}  {d.TotalSize / (1024 * 1024)} MB"); }
                catch { list.Add($"{d.Name}  (not ready)"); }
        return list;
    }
}

// =====================================================================
// USB MONITOR
// =====================================================================
public class UsbTestForm : Form
{
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = Theme.MonoFont, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 800 };
    private HashSet<string> _last = new();
    public int DetectedCount { get; private set; }
    public bool PassedFlag { get; private set; }

    public UsbTestForm()
    {
        Text = "USB port test";
        Width = 760; Height = 460;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;

        var top = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Theme.Bg, Padding = new Padding(24, 16, 24, 8) };
        var lbl = new Label { Text = "Insert a USB stick into each USB port one by one. Detection should appear within 1 s.", AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.BodyFont, Location = new Point(24, 16) };
        top.Controls.Add(lbl);

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(24, 12, 24, 12) };
        center.Controls.Add(_log);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg, Padding = new Padding(24, 10, 24, 10) };
        var btnPass = ModernButton.Make("USB OK", Theme.Success);
        btnPass.Location = new Point(24, 10); btnPass.Width = 160;
        btnPass.Click += (s, e) => { PassedFlag = true; DialogResult = DialogResult.OK; Close(); };
        var btnFail = ModernButton.Make("Mark fault", Theme.Danger);
        btnFail.Location = new Point(196, 10); btnFail.Width = 160;
        btnFail.Click += (s, e) => { PassedFlag = false; DialogResult = DialogResult.OK; Close(); };
        bottom.Controls.Add(btnPass); bottom.Controls.Add(btnFail);

        Controls.Add(center);
        Controls.Add(top);
        Controls.Add(bottom);

        _timer.Tick += (s, e) => Poll();
        _timer.Start();
        Poll();
        FormClosed += (s, e) => _timer.Stop();
    }

    private void Poll()
    {
        var now = new HashSet<string>(SystemInfo.ListRemovable());
        foreach (var n in now.Except(_last)) { _log.AppendText($"[{DateTime.Now:HH:mm:ss}] CONNECTED    {n}\r\n"); DetectedCount++; }
        foreach (var n in _last.Except(now)) _log.AppendText($"[{DateTime.Now:HH:mm:ss}] DISCONNECTED {n}\r\n");
        _last = now;
    }
}

// =====================================================================
// SHELL launchers
// =====================================================================
public static class ShellLaunch
{
    public static void Camera() => Try("microsoft.windows.camera:", "Camera app");
    public static void WifiSettings() => Try("ms-settings:network-wifi", "Wi-Fi settings");
    public static void BluetoothSettings() => Try("ms-settings:bluetooth", "Bluetooth settings");
    public static void DellQuickTest(string serviceTag)
    {
        var url = string.IsNullOrWhiteSpace(serviceTag)
            ? "https://www.dell.com/support/home/cs-cz/quicktest"
            : $"https://www.dell.com/support/home/cs-cz/product-support/servicetag/{serviceTag.Trim()}/diagnose";
        Try(url, "Dell QuickTest");
    }
    public static void DellSupportAssist()
    {
        // try local SupportAssist installs, else open web
        string[] candidates = {
            @"C:\Program Files\Dell\SupportAssistAgent\bin\SupportAssistAgent.exe",
            @"C:\Program Files (x86)\Dell\SupportAssist\SupportAssist.exe",
            @"C:\Program Files\Dell\SupportAssist\SupportAssist.exe",
        };
        foreach (var p in candidates)
            if (File.Exists(p))
            {
                try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); return; }
                catch { }
            }
        Try("https://www.dell.com/support/home/cs-cz/quicktest", "Dell QuickTest");
    }
    private static void Try(string uri, string what)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Cannot open {what}: " + ex.Message); }
    }
}

// =====================================================================
// SENSORS — LibreHardwareMonitor wrapper, real CPU/GPU temps + fan RPM
// =====================================================================
public class Sensors : IDisposable
{
    private LibreHardwareMonitor.Hardware.Computer _computer;

    public Sensors()
    {
        try
        {
            _computer = new LibreHardwareMonitor.Hardware.Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsControllerEnabled = true,
            };
            _computer.Open();
        }
        catch { _computer = null; }
    }

    public void Update()
    {
        if (_computer == null) return;
        foreach (var hw in _computer.Hardware)
        {
            try { hw.Update(); foreach (var sub in hw.SubHardware) sub.Update(); } catch { }
        }
    }

    public double? CpuPackageTempC()
    {
        if (_computer == null) return null;
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Cpu) continue;
            double? pkg = null, max = null;
            foreach (var s in hw.Sensors)
            {
                if (s.SensorType != LibreHardwareMonitor.Hardware.SensorType.Temperature) continue;
                var v = s.Value;
                if (!v.HasValue) continue;
                if (s.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0) pkg = v.Value;
                if (!max.HasValue || v.Value > max.Value) max = v.Value;
            }
            return pkg ?? max;
        }
        return null;
    }

    public double? GpuTempC()
    {
        if (_computer == null) return null;
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia &&
                hw.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.GpuAmd &&
                hw.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.GpuIntel) continue;
            foreach (var s in hw.Sensors)
                if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature && s.Value.HasValue)
                    return s.Value.Value;
        }
        return null;
    }

    public List<(string name, int rpm)> Fans()
    {
        var list = new List<(string, int)>();
        if (_computer == null) return list;
        foreach (var hw in _computer.Hardware)
        {
            foreach (var s in hw.Sensors)
                if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan && s.Value.HasValue && s.Value.Value > 0)
                    list.Add((s.Name, (int)s.Value.Value));
            foreach (var sub in hw.SubHardware)
                foreach (var s in sub.Sensors)
                    if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan && s.Value.HasValue && s.Value.Value > 0)
                        list.Add((s.Name, (int)s.Value.Value));
        }
        return list;
    }

    public double? CpuLoadPercent()
    {
        if (_computer == null) return null;
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Cpu) continue;
            foreach (var s in hw.Sensors)
                if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Load &&
                    s.Name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0 && s.Value.HasValue)
                    return s.Value.Value;
        }
        return null;
    }

    public string Summary()
    {
        Update();
        var parts = new List<string>();
        var cpu = CpuPackageTempC();
        if (cpu.HasValue) parts.Add($"CPU {cpu.Value:F1} °C");
        var gpu = GpuTempC();
        if (gpu.HasValue) parts.Add($"GPU {gpu.Value:F1} °C");
        var fans = Fans();
        if (fans.Count > 0) parts.Add(string.Join(" / ", fans.Select(f => $"{f.name} {f.rpm} RPM")));
        return parts.Count == 0 ? "Sensors unavailable (LibreHardwareMonitor has no data for this HW)" : string.Join("   ", parts);
    }

    public void Dispose()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
    }
}

// =====================================================================
// SMART — disk health via WMI MSStorageDriver_*
// =====================================================================
public static class SmartTest
{
    // Subset of common SMART attribute IDs
    private static readonly Dictionary<byte, string> AttrNames = new()
    {
        { 0x01, "Read Error Rate" },
        { 0x04, "Start/Stop Count" },
        { 0x05, "Reallocated Sectors" },
        { 0x07, "Seek Error Rate" },
        { 0x09, "Power-On Hours" },
        { 0x0A, "Spin-Up Retries" },
        { 0x0C, "Power Cycle Count" },
        { 0xB7, "SATA Downshift Count" },
        { 0xB8, "End-to-End Error" },
        { 0xBB, "Reported Uncorrectable" },
        { 0xBC, "Command Timeout" },
        { 0xBD, "High Fly Writes" },
        { 0xBE, "Airflow Temperature" },
        { 0xBF, "G-Sense Errors" },
        { 0xC0, "Power-Off Retract" },
        { 0xC1, "Load Cycle Count" },
        { 0xC2, "Temperature" },
        { 0xC3, "Hardware ECC Recovered" },
        { 0xC4, "Reallocation Events" },
        { 0xC5, "Current Pending Sectors" },
        { 0xC6, "Offline Uncorrectable" },
        { 0xC7, "UDMA CRC Errors" },
        { 0xE7, "SSD Life Left" },
        { 0xE9, "Media Wearout" },
        { 0xEA, "Total LBAs Written" },
        { 0xF1, "Total Host Writes" },
        { 0xF2, "Total Host Reads" },
    };

    public class Attribute_
    {
        public byte Id;
        public string Name = "";
        public byte Current;
        public byte Worst;
        public byte Threshold;
        public long Raw;
        public bool BelowThreshold;
    }

    public class DriveReport
    {
        public string Instance = "";
        public bool PredictFailure;
        public List<Attribute_> Attributes = new();
        public string Verdict = "OK";
    }

    public static List<DriveReport> Run()
    {
        var reports = new Dictionary<string, DriveReport>();
        try
        {
            using var s = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM MSStorageDriver_FailurePredictStatus");
            foreach (ManagementObject m in s.Get())
            {
                var inst = (string)m["InstanceName"];
                bool predict = false;
                try { predict = (bool)m["PredictFailure"]; } catch { }
                if (!reports.TryGetValue(inst, out var r)) { r = new DriveReport { Instance = inst }; reports[inst] = r; }
                r.PredictFailure = predict;
            }
        }
        catch (Exception ex) { return new List<DriveReport> { new() { Instance = "WMI chyba", Verdict = ex.Message } }; }

        try
        {
            // thresholds
            var thresholds = new Dictionary<string, Dictionary<byte, byte>>();
            using var t = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM MSStorageDriver_FailurePredictThresholds");
            foreach (ManagementObject m in t.Get())
            {
                var inst = (string)m["InstanceName"];
                var vs = (byte[])m["VendorSpecific"];
                var d = new Dictionary<byte, byte>();
                for (int i = 2; i + 12 <= vs.Length; i += 12)
                {
                    byte id = vs[i];
                    if (id == 0) continue;
                    d[id] = vs[i + 1]; // threshold byte
                }
                thresholds[inst] = d;
            }

            using var dataS = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM MSStorageDriver_FailurePredictData");
            foreach (ManagementObject m in dataS.Get())
            {
                var inst = (string)m["InstanceName"];
                var vs = (byte[])m["VendorSpecific"];
                if (!reports.TryGetValue(inst, out var r)) { r = new DriveReport { Instance = inst }; reports[inst] = r; }
                thresholds.TryGetValue(inst, out var dThr);
                for (int i = 2; i + 12 <= vs.Length; i += 12)
                {
                    byte id = vs[i];
                    if (id == 0) continue;
                    var a = new Attribute_
                    {
                        Id = id,
                        Name = AttrNames.TryGetValue(id, out var n) ? n : $"Attr 0x{id:X2}",
                        Current = vs[i + 3],
                        Worst = vs[i + 4],
                    };
                    long raw = 0;
                    for (int j = 0; j < 6; j++) raw |= ((long)vs[i + 5 + j]) << (8 * j);
                    a.Raw = raw;
                    if (dThr != null && dThr.TryGetValue(id, out var thr))
                    {
                        a.Threshold = thr;
                        a.BelowThreshold = a.Current > 0 && a.Current < thr;
                    }
                    r.Attributes.Add(a);
                }
            }
        }
        catch (Exception ex)
        {
            foreach (var r in reports.Values) r.Verdict = "Data nedostupná: " + ex.Message;
        }

        foreach (var r in reports.Values)
        {
            var crit = new List<string>();
            if (r.PredictFailure) crit.Add("PREDIKCE SELHÁNÍ");
            foreach (var a in r.Attributes)
            {
                if (a.BelowThreshold) crit.Add($"{a.Name} pod prahem");
                if ((a.Id == 0x05 || a.Id == 0xC5 || a.Id == 0xC6) && a.Raw > 0) crit.Add($"{a.Name} = {a.Raw}");
                if (a.Id == 0xE9 && a.Current < 30) crit.Add($"Media wearout {a.Current} %");
            }
            r.Verdict = crit.Count == 0 ? "OK" : string.Join(", ", crit);
        }
        return reports.Values.ToList();
    }
}

public class SmartReportForm : Form
{
    public SmartReportForm(List<SmartTest.DriveReport> reports)
    {
        Text = "SMART — disk health";
        Width = 900; Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;

        var tb = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = Theme.MonoFont, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, WordWrap = false };
        var sb = new StringBuilder();
        foreach (var r in reports)
        {
            sb.AppendLine($"========================================================");
            sb.AppendLine($" Device: {r.Instance}");
            sb.AppendLine($" Failure prediction: {(r.PredictFailure ? "YES — disk is failing!" : "no")}");
            sb.AppendLine($" Verdict: {r.Verdict}");
            sb.AppendLine($"========================================================");
            sb.AppendLine($"  ID    Attribute                     Current  Worst  Thr  Raw");
            sb.AppendLine($"  ---   ----------------------------  -------  -----  ---  ----------");
            foreach (var a in r.Attributes)
            {
                var flag = a.BelowThreshold ? " ⚠" : "";
                sb.AppendLine($"  0x{a.Id:X2}  {a.Name,-28}  {a.Current,7}  {a.Worst,5}  {a.Threshold,3}  {a.Raw,10}{flag}");
            }
            sb.AppendLine();
        }
        tb.Text = sb.ToString();
        Controls.Add(tb);
    }
}

// =====================================================================
// DISK BENCHMARK — write+read 256 MB to temp, measure throughput
// =====================================================================
public static class DiskBenchmark
{
    public static (double writeMBps, double readMBps, string log) Run(int sizeMB = 256, Action<string> progress = null)
    {
        var path = Path.Combine(Path.GetTempPath(), "kp_disk_bench.tmp");
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        var buf = new byte[1024 * 1024];
        new Random(42).NextBytes(buf);

        // Write
        progress?.Invoke("Zápis...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.WriteThrough))
        {
            for (int i = 0; i < sizeMB; i++) fs.Write(buf, 0, buf.Length);
            fs.Flush(true);
        }
        sw.Stop();
        var writeMBps = sizeMB / sw.Elapsed.TotalSeconds;

        // Read (FILE_FLAG_NO_BUFFERING via FileOptions — partial cache bypass)
        progress?.Invoke("Čtení...");
        sw.Restart();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
        {
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0) { }
        }
        sw.Stop();
        var readMBps = sizeMB / sw.Elapsed.TotalSeconds;

        try { File.Delete(path); } catch { }

        var verdict = (writeMBps >= 50 && readMBps >= 100) ? "OK (SSD level)" :
                      (readMBps >= 50) ? "average (HDD or old SSD)" :
                      "SUSPICIOUSLY SLOW — disk may be failing / worn out";
        var log = $"Write: {writeMBps:F1} MB/s   Read: {readMBps:F1} MB/s   →  {verdict}";
        return (writeMBps, readMBps, log);
    }
}

// =====================================================================
// FULL DISK BENCHMARK — reads random 1 MB chunks across the entire
// physical drive via \\.\PhysicalDrive0. Detects bad sectors and
// measures sustained random read throughput across the whole disk.
// =====================================================================
public class FullDiskBenchmarkForm : Form
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        Microsoft.Win32.SafeHandles.SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        [Out] byte[] lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    private const uint GENERIC_READ = 0x80000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint IOCTL_DISK_GET_LENGTH_INFO = 0x7405C;

    private readonly Label _phase = new() { Font = Theme.TitleFont, AutoSize = true, ForeColor = Theme.Text };
    private readonly Label _detail = new() { Font = Theme.BodyFont, AutoSize = true, ForeColor = Theme.TextMuted };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 8 };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = Theme.MonoFont, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };

    public bool PassedFlag { get; private set; }
    public string ResultText { get; private set; } = "";
    private volatile bool _cancel;

    public FullDiskBenchmarkForm()
    {
        Text = "Full disk benchmark (whole drive surface scan)";
        Width = 820; Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        Padding = new Padding(8);

        var top = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Theme.Bg, Padding = new Padding(28, 18, 28, 8) };
        _phase.Location = new Point(28, 16); _phase.Text = "Starting…";
        _detail.Location = new Point(28, 48);
        _bar.Top = 80; _bar.Left = 28; _bar.Width = 740; _bar.Maximum = 100;
        top.Controls.Add(_phase); top.Controls.Add(_detail); top.Controls.Add(_bar);

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(24, 12, 24, 12) };
        center.Controls.Add(_log);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg, Padding = new Padding(28, 10, 28, 10) };
        var btnCancel = ModernButton.Make("Cancel", Theme.Danger, 140, 36);
        btnCancel.Location = new Point(28, 10);
        btnCancel.Click += (s, e) => { _cancel = true; Log("Cancelled by user."); };
        bottom.Controls.Add(btnCancel);

        Controls.Add(center); Controls.Add(top); Controls.Add(bottom);

        Load += (s, e) => System.Threading.Tasks.Task.Run(RunBench);
        FormClosing += (s, e) => _cancel = true;
    }

    private void RunBench()
    {
        try { RunBenchInner(); }
        catch (Exception ex)
        {
            Log("FATAL: " + ex.Message);
            ResultText = "Error: " + ex.Message;
            PassedFlag = false;
        }
        BeginInvoke(() => { DialogResult = DialogResult.OK; Close(); });
    }

    private void RunBenchInner()
    {
        SetPhase("Opening physical drive…");
        using var h = CreateFileW(@"\\.\PhysicalDrive0",
            GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (h.IsInvalid)
        {
            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Log($"Cannot open \\\\.\\PhysicalDrive0  (Win32 error {err}). Need admin.");
            ResultText = "Cannot open physical drive (admin needed)";
            PassedFlag = false;
            return;
        }

        // Disk size
        var lenBuf = new byte[8];
        if (!DeviceIoControl(h, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, lenBuf, 8, out _, IntPtr.Zero))
        {
            Log("Cannot read disk length via IOCTL.");
            ResultText = "Cannot determine disk size";
            PassedFlag = false;
            return;
        }
        long diskSize = BitConverter.ToInt64(lenBuf, 0);
        double diskGB = diskSize / (1024.0 * 1024 * 1024);
        Log($"Physical drive size: {diskGB:F1} GB");

        SetPhase("Reading random sectors across the drive…");
        using var fs = new FileStream(h, FileAccess.Read, 1024 * 1024);
        const int chunkSize = 1024 * 1024;       // 1 MB per chunk
        const int chunks    = 500;               // 500 chunks → samples whole disk surface
        var buf = new byte[chunkSize];
        var rng = new Random();
        long totalRead = 0;
        int errors = 0;
        var errorPositions = new List<long>();
        double minSpeed = double.MaxValue, maxSpeed = 0, sumSpeed = 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < chunks; i++)
        {
            if (_cancel) break;
            // Pick random sector-aligned position spanning the whole disk
            long maxOffset = diskSize - chunkSize;
            long pos = (long)(rng.NextDouble() * maxOffset);
            pos = (pos / 4096) * 4096;  // align to 4 KB sector

            var swc = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                fs.Position = pos;
                int read = 0;
                while (read < chunkSize)
                {
                    int n = fs.Read(buf, read, chunkSize - read);
                    if (n <= 0) break;
                    read += n;
                }
                swc.Stop();
                if (read == chunkSize)
                {
                    totalRead += read;
                    double mbps = (read / (1024.0 * 1024)) / swc.Elapsed.TotalSeconds;
                    if (mbps < minSpeed) minSpeed = mbps;
                    if (mbps > maxSpeed) maxSpeed = mbps;
                    sumSpeed += mbps;
                }
                else { errors++; errorPositions.Add(pos); }
            }
            catch (Exception ex)
            {
                errors++;
                errorPositions.Add(pos);
                Log($"  ⚠ Read error at {pos / (1024L * 1024 * 1024):F1} GB:  {ex.Message}");
            }

            if (i % 10 == 0 || i == chunks - 1)
            {
                SetProgress((i + 1) * 100 / chunks, $"{i + 1}/{chunks} chunks  •  {totalRead / (1024 * 1024)} MB read  •  {errors} error(s)");
            }
        }
        sw.Stop();

        var elapsedS = sw.Elapsed.TotalSeconds;
        var avgSpeed = chunks > 0 ? sumSpeed / chunks : 0;
        var overallSpeed = totalRead / (1024.0 * 1024) / elapsedS;

        Log("");
        Log($"=== Surface scan complete ===");
        Log($"Disk size:        {diskGB:F1} GB");
        Log($"Chunks read:      {chunks}  ({totalRead / (1024 * 1024)} MB sampled across whole disk)");
        Log($"Duration:         {elapsedS:F1} s");
        Log($"Overall speed:    {overallSpeed:F1} MB/s");
        Log($"Per-chunk speed:  min {minSpeed:F1} / avg {avgSpeed:F1} / max {maxSpeed:F1} MB/s");
        Log($"Read errors:      {errors}");
        if (errors > 0)
        {
            Log("⚠ BAD SECTORS DETECTED at offsets:");
            foreach (var p in errorPositions.Take(20)) Log($"   {p / (1024L * 1024 * 1024):F2} GB");
            if (errorPositions.Count > 20) Log($"   ... +{errorPositions.Count - 20} more");
        }

        PassedFlag = errors == 0 && avgSpeed >= 30;  // 30 MB/s random read floor
        ResultText = errors > 0
            ? $"FAIL — {errors} bad sectors across {diskGB:F1} GB"
            : avgSpeed < 30
                ? $"SLOW — avg {avgSpeed:F1} MB/s (HDD or worn SSD)"
                : $"OK — {chunks}×1MB scanned, no errors, avg {avgSpeed:F1} MB/s";
        Log("");
        Log(PassedFlag ? "Verdict: PASS" : "Verdict: " + ResultText);
        SetPhase(PassedFlag ? "DONE — no errors" : "DONE — issues found");
    }

    private void SetPhase(string s) { if (IsHandleCreated) BeginInvoke(() => _phase.Text = s); }
    private void SetProgress(int pct, string detail)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() => { _bar.Value = Math.Clamp(pct, 0, 100); _detail.Text = detail; });
    }
    private void Log(string s) { if (IsHandleCreated) BeginInvoke(() => _log.AppendText(s + "\r\n")); }
}

// =====================================================================
// MEMORY PATTERN TEST
// =====================================================================
public class MemoryTestForm : Form
{
    private readonly Label _phase = new() { Font = Theme.TitleFont, AutoSize = true, ForeColor = Theme.Text };
    private readonly Label _detail = new() { Font = Theme.BodyFont, AutoSize = true, ForeColor = Theme.TextMuted };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 8 };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = Theme.MonoFont, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
    public bool PassedFlag { get; private set; }
    public string ResultText { get; private set; } = "";

    public MemoryTestForm()
    {
        Text = "Memory test (RAM)";
        Width = 780; Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        Padding = new Padding(8);

        var top = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Theme.Bg, Padding = new Padding(28, 18, 28, 8) };
        _phase.Location = new Point(28, 16); _phase.Text = "Starting…";
        _detail.Location = new Point(28, 48);
        _bar.Top = 80; _bar.Left = 28; _bar.Width = 720; _bar.Maximum = 100;
        top.Controls.Add(_phase); top.Controls.Add(_detail); top.Controls.Add(_bar);

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(24, 12, 24, 12) };
        center.Controls.Add(_log);

        Controls.Add(center); Controls.Add(top);

        Load += (s, e) => System.Threading.Tasks.Task.Run(RunTest);
    }

    private void RunTest()
    {
        // Allocate ~50 % of available RAM (don't OOM the system)
        var memInfo = GC.GetGCMemoryInfo();
        long target = memInfo.TotalAvailableMemoryBytes / 2;
        target = Math.Min(target, 4L * 1024 * 1024 * 1024); // cap at 4 GB
        long blockSize = 16 * 1024 * 1024;
        var blocks = new List<byte[]>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int errors = 0;

        try
        {
            // Allocate
            SetPhase("Allocating RAM…");
            long alloc = 0;
            while (alloc < target)
            {
                long thisBlock = Math.Min(blockSize, target - alloc);
                try { blocks.Add(new byte[thisBlock]); alloc += thisBlock; }
                catch (OutOfMemoryException) { break; }
                if (blocks.Count % 4 == 0) SetProgress((int)(alloc * 100 / target), $"{alloc / (1024 * 1024)} MB");
            }
            Log($"Allocated {alloc / (1024 * 1024)} MB in {blocks.Count} blocks.");

            // Patterns
            errors += DoPattern(blocks, "Pattern 0xAA", 0xAA);
            errors += DoPattern(blocks, "Pattern 0x55", 0x55);
            errors += DoPattern(blocks, "Pattern 0xFF", 0xFF);
            errors += DoPattern(blocks, "Pattern 0x00", 0x00);
            errors += DoPseudoRandom(blocks);

            sw.Stop();
            PassedFlag = errors == 0;
            ResultText = errors == 0
                ? $"OK — {alloc / (1024 * 1024)} MB tested, no errors ({sw.Elapsed.TotalSeconds:F0}s)"
                : $"FAIL — {errors} bit errors in {alloc / (1024 * 1024)} MB. RAM likely faulty.";
            SetPhase(PassedFlag ? "DONE — no errors" : "DONE — ERRORS!");
            Log("");
            Log(ResultText);
        }
        finally
        {
            blocks.Clear(); GC.Collect(); GC.WaitForPendingFinalizers();
        }

        BeginInvoke(() => { DialogResult = DialogResult.OK; Close(); });
    }

    private int DoPattern(List<byte[]> blocks, string name, byte pattern)
    {
        SetPhase("Writing " + name);
        Log($"[{DateTime.Now:HH:mm:ss}] Writing {name} (0x{pattern:X2})");
        for (int b = 0; b < blocks.Count; b++)
        {
            Array.Fill(blocks[b], pattern);
            if (b % 16 == 0) SetProgress(b * 100 / blocks.Count, $"{b}/{blocks.Count} blocks");
        }
        SetPhase("Verifying " + name);
        Log($"[{DateTime.Now:HH:mm:ss}] Verifying {name}");
        int errors = 0;
        for (int b = 0; b < blocks.Count; b++)
        {
            var arr = blocks[b];
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != pattern) errors++;
            if (b % 16 == 0) SetProgress(b * 100 / blocks.Count, $"{b}/{blocks.Count} blocks");
        }
        if (errors > 0) Log($"  ⚠ {errors} errors!");
        return errors;
    }

    private int DoPseudoRandom(List<byte[]> blocks)
    {
        SetPhase("Writing pseudo-random pattern");
        Log($"[{DateTime.Now:HH:mm:ss}] Writing pseudo-random pattern");
        for (int b = 0; b < blocks.Count; b++)
        {
            var rng = new Random(b * 37 + 1);
            rng.NextBytes(blocks[b]);
            if (b % 16 == 0) SetProgress(b * 100 / blocks.Count, $"{b}/{blocks.Count} blocks");
        }
        SetPhase("Verifying pseudo-random pattern");
        int errors = 0;
        for (int b = 0; b < blocks.Count; b++)
        {
            var rng = new Random(b * 37 + 1);
            var exp = new byte[blocks[b].Length];
            rng.NextBytes(exp);
            for (int i = 0; i < exp.Length; i++)
                if (blocks[b][i] != exp[i]) errors++;
            if (b % 16 == 0) SetProgress(b * 100 / blocks.Count, $"{b}/{blocks.Count} blocks");
        }
        if (errors > 0) Log($"  ⚠ {errors} errors!");
        return errors;
    }

    private void SetPhase(string s) { if (IsHandleCreated) BeginInvoke(() => _phase.Text = s); }
    private void SetProgress(int pct, string detail)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() => { _bar.Value = Math.Clamp(pct, 0, 100); _detail.Text = detail; });
    }
    private void Log(string s) { if (IsHandleCreated) BeginInvoke(() => _log.AppendText(s + "\r\n")); }
}

// =====================================================================
// NETWORK PING TEST
// =====================================================================
public static class NetworkPing
{
    public static (bool ok, string text) Run()
    {
        var targets = new List<(string name, string host)>
        {
            ("Gateway", DetectGateway()),
            ("Google DNS", "8.8.8.8"),
            ("Cloudflare", "1.1.1.1"),
            ("google.com", "google.com"),
        };
        var lines = new List<string>();
        int ok = 0, total = 0;
        var p = new System.Net.NetworkInformation.Ping();
        foreach (var (name, host) in targets)
        {
            if (string.IsNullOrEmpty(host)) { lines.Add($"{name,-12} —"); continue; }
            total++;
            try
            {
                var reply = p.Send(host, 1500);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                { ok++; lines.Add($"{name,-12} {host,-16}  {reply.RoundtripTime} ms ✓"); }
                else
                    lines.Add($"{name,-12} {host,-16}  {reply.Status} ✗");
            }
            catch (Exception ex) { lines.Add($"{name,-12} {host,-16}  CHYBA: {ex.Message}"); }
        }
        return (ok == total && total > 0, string.Join("\n", lines));
    }

    private static string DetectGateway()
    {
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            var gw = ni.GetIPProperties().GatewayAddresses;
            foreach (var g in gw) if (g.Address != null && g.Address.ToString() != "0.0.0.0") return g.Address.ToString();
        }
        return null;
    }
}

// =====================================================================
// WEBCAM PREVIEW — AForge.Video.DirectShow
// =====================================================================
public class WebcamTestForm : Form
{
    private AForge.Video.DirectShow.VideoCaptureDevice _device;
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly Label _info = new() { AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.BodyFont };
    public bool PassedFlag { get; private set; }

    public WebcamTestForm()
    {
        Text = "Webcam test";
        Width = 760; Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;

        var top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Bg, Padding = new Padding(24, 16, 24, 8) };
        _info.Location = new Point(24, 18);
        top.Controls.Add(_info);

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(20) };
        center.Controls.Add(_preview);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg, Padding = new Padding(24, 10, 24, 10) };
        var btnPass = ModernButton.Make("Image visible — OK", Theme.Success, 200, 36);
        btnPass.Location = new Point(24, 10);
        btnPass.Click += (s, e) => { PassedFlag = true; DialogResult = DialogResult.OK; Close(); };
        var btnFail = ModernButton.Make("Fault / no image", Theme.Danger, 200, 36);
        btnFail.Location = new Point(236, 10);
        btnFail.Click += (s, e) => { PassedFlag = false; DialogResult = DialogResult.OK; Close(); };
        bottom.Controls.Add(btnPass); bottom.Controls.Add(btnFail);

        Controls.Add(center); Controls.Add(top); Controls.Add(bottom);

        Load += (s, e) => Start();
        FormClosing += (s, e) => Stop();
    }

    private void Start()
    {
        try
        {
            var devices = new AForge.Video.DirectShow.FilterInfoCollection(AForge.Video.DirectShow.FilterCategory.VideoInputDevice);
            if (devices.Count == 0)
            {
                _info.Text = "No video device found.";
                _info.ForeColor = Theme.Danger;
                return;
            }
            _device = new AForge.Video.DirectShow.VideoCaptureDevice(devices[0].MonikerString);
            _device.NewFrame += (sender, eventArgs) =>
            {
                try
                {
                    var bmp = (Bitmap)eventArgs.Frame.Clone();
                    BeginInvoke(() =>
                    {
                        _preview.Image?.Dispose();
                        _preview.Image = bmp;
                    });
                }
                catch { }
            };
            _device.Start();
            _info.Text = $"Device: {devices[0].Name}";
        }
        catch (Exception ex) { _info.Text = "Camera start error: " + ex.Message; _info.ForeColor = Theme.Danger; }
    }

    private void Stop()
    {
        try { _device?.SignalToStop(); _device?.WaitForStop(); } catch { }
        try { _preview.Image?.Dispose(); } catch { }
    }
}

// =====================================================================
// CPU STRESS TEST — multi-thread busy loop + thermal monitoring
// =====================================================================
public class StressTestForm : Form
{
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly Label _elapsed = new() { Font = new Font("Consolas", 28, FontStyle.Bold), AutoSize = true, ForeColor = Theme.Accent };
    private readonly Label _temp = new() { Font = new Font("Consolas", 28, FontStyle.Bold), AutoSize = true, ForeColor = Theme.Success };
    private readonly Label _cpu = new() { Font = new Font("Consolas", 28, FontStyle.Bold), AutoSize = true, ForeColor = Theme.Accent };
    private readonly Label _max = new() { Font = Theme.BodyFont, AutoSize = true, ForeColor = Theme.TextMuted };
    private readonly Label _threads = new() { Font = Theme.BodyFont, AutoSize = true, ForeColor = Theme.TextMuted };
    private readonly TextBox _log = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Dock = DockStyle.Fill, Font = Theme.MonoFont, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 8 };
    private readonly List<System.Threading.Thread> _workers = new();
    private volatile bool _stop;
    private DateTime _start;
    private double _maxC;
    private double _maxCpu;
    private readonly int _seconds;
    private System.Diagnostics.PerformanceCounter _cpuCounter;
    public bool PassedFlag { get; private set; }
    public double MaxTempReached => _maxC;
    public double MaxCpuReached => _maxCpu;

    public StressTestForm(int seconds = 120)
    {
        _seconds = seconds;
        Text = $"CPU stress test — {seconds} s";
        Width = 880; Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Padding = new Padding(8);

        var top = new Panel { Dock = DockStyle.Top, Height = 200, BackColor = Theme.Bg, Padding = new Padding(28, 18, 28, 8) };
        var lblE = new Label { Text = "TIME", AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.PillFont, Location = new Point(28, 18) };
        _elapsed.Location = new Point(28, 36); _elapsed.Text = "0:00";
        var lblC = new Label { Text = "CPU LOAD", AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.PillFont, Location = new Point(220, 18) };
        _cpu.Location = new Point(220, 36); _cpu.Text = "—";
        var lblT = new Label { Text = "TEMPERATURE", AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.PillFont, Location = new Point(440, 18) };
        _temp.Location = new Point(440, 36); _temp.Text = "— °C";
        _max.Location = new Point(28, 105);
        _threads.Location = new Point(28, 130);
        _bar.Top = 175; _bar.Width = 820; _bar.Left = 28; _bar.Maximum = seconds;
        top.Controls.Add(lblE); top.Controls.Add(_elapsed);
        top.Controls.Add(lblC); top.Controls.Add(_cpu);
        top.Controls.Add(lblT); top.Controls.Add(_temp);
        top.Controls.Add(_max); top.Controls.Add(_threads); top.Controls.Add(_bar);

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(24, 12, 24, 12) };
        center.Controls.Add(_log);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg, Padding = new Padding(28, 10, 28, 10) };
        var btnStop = ModernButton.Make("Stop and evaluate", Theme.Danger, 220, 36);
        btnStop.Location = new Point(28, 10);
        btnStop.Click += (s, e) => Finish();
        bottom.Controls.Add(btnStop);

        Controls.Add(center); Controls.Add(top); Controls.Add(bottom);

        try { _cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total"); _cpuCounter.NextValue(); } catch { _cpuCounter = null; }

        Load += (s, e) => StartStress();
        FormClosing += (s, e) => StopWorkers();
    }

    private void StartStress()
    {
        int n = Environment.ProcessorCount;
        _threads.Text = $"Workers: {n} (one per logical core) — if CPU temp does not rise, sensor not accessible via WMI";
        _start = DateTime.Now;
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] Start — {n} workers, plan {_seconds} s\r\n");
        for (int i = 0; i < n; i++)
        {
            var t = new System.Threading.Thread(BusyLoop)
            {
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.Normal,
                Name = $"StressWorker{i}"
            };
            _workers.Add(t);
            t.Start();
        }
        _tick.Tick += (s, e) => Tick();
        _tick.Start();
    }

    // Anti-optimization: heavy floating point + integer + memory pattern.
    private static long _sinkBits;
    private void BusyLoop()
    {
        double acc = 1.0001;
        long ai = 1;
        var buf = new double[4096];
        for (int i = 0; i < buf.Length; i++) buf[i] = i * 0.7;
        while (!_stop)
        {
            for (int i = 0; i < 100000; i++)
            {
                acc = Math.Sqrt(acc * 3.14159) + Math.Sin(acc) + Math.Cos(acc * 0.5);
                ai = (ai * 1103515245 + 12345) & 0x7FFFFFFF;
                buf[ai & 0xFFF] = acc;
                acc += buf[(ai >> 4) & 0xFFF] * 0.0001;
                if (double.IsNaN(acc) || double.IsInfinity(acc)) acc = 1.0001;
            }
            System.Threading.Interlocked.Exchange(ref _sinkBits, BitConverter.DoubleToInt64Bits(acc));
        }
    }

    private void Tick()
    {
        var s = (int)(DateTime.Now - _start).TotalSeconds;
        _elapsed.Text = $"{s / 60}:{s % 60:00}";
        _bar.Value = Math.Min(s, _seconds);

        // CPU %
        double cpuPct = 0;
        try { if (_cpuCounter != null) cpuPct = _cpuCounter.NextValue(); } catch { }
        _cpu.Text = $"{cpuPct:F0} %";
        _cpu.ForeColor = cpuPct >= 80 ? Theme.Success : cpuPct >= 50 ? Theme.Warning : Theme.Danger;
        if (cpuPct > _maxCpu) _maxCpu = cpuPct;

        // Real CPU temperature via LibreHardwareMonitor
        double cur = 0;
        int fanRpm = 0;
        try
        {
            SystemInfo.SharedSensors.Update();
            var t = SystemInfo.SharedSensors.CpuPackageTempC();
            if (t.HasValue) cur = t.Value;
            var fans = SystemInfo.SharedSensors.Fans();
            if (fans.Count > 0) fanRpm = fans[0].rpm;
        }
        catch { }

        if (cur > 0)
        {
            _temp.Text = $"{cur:F1} °C";
            _temp.ForeColor = cur < 75 ? Theme.Success : cur < 90 ? Theme.Warning : Theme.Danger;
            if (cur > _maxC) _maxC = cur;
        }
        else _temp.Text = "n/a";

        var fanStr = fanRpm > 0 ? $"    Fan: {fanRpm} RPM" : "";
        _max.Text = $"Max CPU: {_maxCpu:F0} %    Max temp: {(_maxC > 0 ? _maxC.ToString("F1") + " °C" : "n/a")}    Current: {(cur > 0 ? cur.ToString("F1") + " °C" : "n/a")}{fanStr}";

        if (s % 5 == 0)
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] t={s,3}s  cpu={cpuPct,5:F0}%  temp={(cur > 0 ? cur.ToString("F1") + "°C" : "n/a"),-8}  fan={(fanRpm > 0 ? fanRpm + " RPM" : "n/a"),-9}  maxCpu={_maxCpu:F0}%\r\n");

        if (s >= _seconds) Finish();
    }

    private void Finish()
    {
        if (_stop) return;
        _tick.Stop();
        StopWorkers();
        var dur = (DateTime.Now - _start).TotalSeconds;
        // Pass: CPU stayed under load (>=80 % at peak) AND (temp < 95 °C OR temp unavailable)
        bool cpuOk = _maxCpu >= 75;
        bool tempOk = _maxC == 0 || _maxC < 95;
        var ok = cpuOk && tempOk;
        _log.AppendText($"\r\n[{DateTime.Now:HH:mm:ss}] DONE — duration {dur:F0}s, max CPU {_maxCpu:F0}%, max temp {(_maxC > 0 ? _maxC.ToString("F1") + "°C" : "n/a")}\r\n");
        if (!cpuOk) _log.AppendText("⚠ CPU load did not reach 75 % — workers may not be running properly\r\n");
        if (_maxC == 0) _log.AppendText("ℹ Temperature sensor not accessible via WMI. LibreHardwareMonitor may have failed. Check via Dell SupportAssist / BIOS.\r\n");
        _log.AppendText(ok ? "Result: PASS\r\n" : "Result: check (possible throttling or worker issue)\r\n");
        PassedFlag = ok;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void StopWorkers()
    {
        _stop = true;
        foreach (var t in _workers) try { t.Join(500); } catch { }
        try { _cpuCounter?.Dispose(); } catch { }
    }
}

// =====================================================================
// LAPTOP RESULT — serializable result file (one per tested laptop)
// =====================================================================
public class LaptopResult
{
    public string ServiceTag { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Tester { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string Ram { get; set; } = "";
    public string Storage { get; set; } = "";
    public DateTime TestedAt { get; set; }
    public int TestsPass { get; set; }
    public int TestsFail { get; set; }
    public int TestsTotal { get; set; }
    public List<string> Failed { get; set; } = new();
    public List<string> VisualUnchecked { get; set; } = new();

    // Concise spec line for the Excel "Specifikace" column, e.g. "i7-1265U / 16 GB / 465 GB"
    public string Spec
    {
        get
        {
            var parts = new[] { Cpu, Ram, Storage }.Where(p => !string.IsNullOrWhiteSpace(p));
            return string.Join(" / ", parts);
        }
    }

    public string Summary => $"Testy: {TestsPass}/{TestsTotal} OK" + (TestsFail > 0 ? $", {TestsFail} vad" : "") + (Failed.Count > 0 ? " — " + string.Join("; ", Failed) : "") + (string.IsNullOrWhiteSpace(Notes) ? "" : ". " + Notes);
}

public static class ResultStore
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string DefaultDir()
    {
        var exeDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(exeDir)) exeDir = Environment.CurrentDirectory;
        var dir = Path.Combine(exeDir, "results");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Save(LaptopResult r, string dir = null)
    {
        dir ??= DefaultDir();
        Directory.CreateDirectory(dir);
        var safe = string.IsNullOrWhiteSpace(r.ServiceTag) ? $"unknown_{DateTime.Now:yyyyMMddHHmmss}" : r.ServiceTag.Trim();
        var path = Path.Combine(dir, $"result-{safe}.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(r, JsonOpts));
        return path;
    }

    public static List<(string file, LaptopResult result, string error)> LoadAll(string dir)
    {
        var list = new List<(string, LaptopResult, string)>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.EnumerateFiles(dir, "result-*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var r = System.Text.Json.JsonSerializer.Deserialize<LaptopResult>(File.ReadAllText(f));
                if (r != null) list.Add((f, r, null));
            }
            catch (Exception ex) { list.Add((f, null, ex.Message)); }
        }
        return list;
    }
}

// =====================================================================
// EXCEL EXPORT — find row by Service Tag; update Stav + Specs, or append
// a new row (copying formulas from the last data row so prices compute).
// =====================================================================
public static class ExcelExport
{
    // Detected inventory layout
    private class Layout
    {
        public ClosedXML.Excel.IXLWorksheet Sheet;
        public int HeaderRow, TagCol, StatusCol;
        public int NotesCol, ModelCol, SpecCol, NumCol;
        public int ProbeCol;     // a price-formula column used to identify data rows
        public int LastDataRow;  // last row that is a real laptop entry
        public int MaxCol;
    }

    private static Layout Detect(ClosedXML.Excel.XLWorkbook wb, out string error)
    {
        error = null;
        var sheet = wb.Worksheets.FirstOrDefault(w => w.Name.IndexOf("Invent", StringComparison.OrdinalIgnoreCase) >= 0)
                 ?? wb.Worksheets.FirstOrDefault();
        if (sheet == null) { error = "No sheet found"; return null; }

        int headerRow = 0, tagCol = 0, statusCol = 0, notesCol = 0, modelCol = 0, specCol = 0, numCol = 0, maxCol = 0;
        int scanTo = Math.Min(10, sheet.LastRowUsed()?.RowNumber() ?? 10);
        for (int r = 1; r <= scanTo; r++)
        {
            int tCol = 0, sCol = 0, nCol = 0, mCol = 0, spCol = 0, nuCol = 0, mx = 0;
            var row = sheet.Row(r);
            for (int c = 1; c <= 40; c++)
            {
                var val = row.Cell(c).GetString().Trim();
                if (string.IsNullOrEmpty(val)) continue;
                mx = c;
                var lower = val.ToLowerInvariant();
                if (tCol == 0 && (lower.Contains("service") || lower.Contains("tag") || lower.Contains("sériov") || lower.Contains("seriov"))) tCol = c;
                if (sCol == 0 && lower == "stav") sCol = c;
                if (nCol == 0 && (lower.Contains("pozn") || lower.Contains("note"))) nCol = c;
                if (mCol == 0 && lower == "model") mCol = c;
                if (spCol == 0 && (lower.Contains("specifik") || lower.Contains("spec") || lower.Contains("konfig"))) spCol = c;
                if (nuCol == 0 && (lower == "č." || lower == "c." || lower == "č" || lower == "#" || lower.Contains("pořad") || lower.Contains("porad"))) nuCol = c;
            }
            if (tCol > 0 && sCol > 0)
            {
                headerRow = r; tagCol = tCol; statusCol = sCol; notesCol = nCol;
                modelCol = mCol; specCol = spCol; numCol = nuCol; maxCol = Math.Max(mx, sCol);
                break;
            }
        }
        if (headerRow == 0) { error = "Columns 'Service Tag' and 'Stav' not found in header"; return null; }

        int lastUsed = sheet.LastRowUsed()?.RowNumber() ?? headerRow;

        // Probe formula column = first column in the first data row that holds a formula.
        // Real laptop rows have price formulas; summary/legend rows below them don't.
        int probeCol = 0;
        for (int c = 1; c <= 40; c++)
            if (sheet.Cell(headerRow + 1, c).HasFormula) { probeCol = c; break; }

        int lastData = headerRow;
        if (probeCol > 0)
        {
            for (int r = headerRow + 1; r <= lastUsed; r++)
                if (sheet.Cell(r, probeCol).HasFormula) lastData = r;
        }
        else
        {
            // No formulas — fall back to last row with a non-empty tag
            for (int r = headerRow + 1; r <= lastUsed; r++)
                if (!string.IsNullOrWhiteSpace(sheet.Cell(r, tagCol).GetString())) lastData = r;
        }

        return new Layout
        {
            Sheet = sheet, HeaderRow = headerRow, TagCol = tagCol, StatusCol = statusCol,
            NotesCol = notesCol, ModelCol = modelCol, SpecCol = specCol, NumCol = numCol,
            ProbeCol = probeCol, LastDataRow = lastData,
            MaxCol = Math.Max(maxCol, 15),
        };
    }

    // Writes one laptop's data into the given row (status, spec, notes; model only on new rows).
    private static void WriteRow(Layout L, int row, LaptopResult res, bool isNewRow)
    {
        // An empty condition must not wipe a status that is already in the sheet.
        if (!string.IsNullOrWhiteSpace(res.Status))
            L.Sheet.Cell(row, L.StatusCol).Value = res.Status;

        // Existing rows keep their curated model name ("Dell Latitude 5400"); the WMI
        // string ("Dell Inc. Latitude 5400") is only used to seed a brand-new row.
        if (isNewRow && L.ModelCol > 0 && !string.IsNullOrWhiteSpace(res.Model))
            L.Sheet.Cell(row, L.ModelCol).Value = res.Model;
        if (L.SpecCol > 0 && !string.IsNullOrWhiteSpace(res.Spec))
            L.Sheet.Cell(row, L.SpecCol).Value = res.Spec;

        if (L.NotesCol > 0)
        {
            var stamp = res.TestedAt == default ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : res.TestedAt.ToString("yyyy-MM-dd HH:mm");
            var line = $"[{stamp}] {res.Summary}";
            var existing = isNewRow ? "" : L.Sheet.Cell(row, L.NotesCol).GetString();
            L.Sheet.Cell(row, L.NotesCol).Value = string.IsNullOrEmpty(existing) ? line : existing + "\n" + line;
        }
    }

    // Creates a brand-new row for a Service Tag not yet in the inventory.
    // Inserts a row ABOVE the current last data row so that summary formulas
    // (e.g. COUNTA(B2:B29)) expand to include it, then copies the shifted
    // original row's formulas/formatting (ClosedXML adjusts relative refs).
    private static int AppendRow(Layout L, LaptopResult res)
    {
        bool haveTemplate = L.ProbeCol > 0 && L.LastDataRow > L.HeaderRow;

        int writeRow;
        if (haveTemplate)
        {
            writeRow = L.LastDataRow;                    // insert here; old content shifts down
            L.Sheet.Row(writeRow).InsertRowsAbove(1);
            // original last-data row is now at writeRow+1 — copy its formulas & formatting
            L.Sheet.Row(writeRow + 1).CopyTo(L.Sheet.Row(writeRow));

            // Row number (Č.) = max so far + 1
            if (L.NumCol > 0)
            {
                var prev = L.Sheet.Cell(writeRow + 1, L.NumCol).GetString();
                if (int.TryParse(prev, out var n)) L.Sheet.Cell(writeRow, L.NumCol).Value = n + 1;
            }

            // Clear sale-tracking text the template carried over (Prodáno komu, Datum),
            // keep formulas (prices) and numeric inputs (market price guess).
            for (int c = 1; c <= L.MaxCol; c++)
            {
                if (c == L.TagCol || c == L.StatusCol || c == L.NotesCol ||
                    c == L.ModelCol || c == L.SpecCol || c == L.NumCol) continue;
                var cell = L.Sheet.Cell(writeRow, c);
                if (cell.HasFormula) continue;
                if (cell.DataType == ClosedXML.Excel.XLDataType.Text)
                    cell.Clear(ClosedXML.Excel.XLClearOptions.Contents);
            }

            L.LastDataRow = writeRow + 1;                // shifted original is the new last
        }
        else
        {
            // No formula template — just write after the last row
            writeRow = L.LastDataRow + 1;
            if (L.NumCol > 0) L.Sheet.Cell(writeRow, L.NumCol).Value = writeRow - L.HeaderRow;
            L.LastDataRow = writeRow;
        }

        L.Sheet.Cell(writeRow, L.TagCol).Value = res.ServiceTag ?? "";
        WriteRow(L, writeRow, res, isNewRow: true);
        return writeRow;
    }

    public static (bool ok, string message) UpdateOrAppend(string xlsxPath, LaptopResult res, bool allowAppend)
    {
        if (!File.Exists(xlsxPath)) return (false, "File does not exist");
        if (string.IsNullOrWhiteSpace(res.ServiceTag)) return (false, "Service Tag is empty");

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(xlsxPath);
            var L = Detect(wb, out var err);
            if (L == null) return (false, err);

            for (int r = L.HeaderRow + 1; r <= L.LastDataRow; r++)
            {
                var cellTag = L.Sheet.Cell(r, L.TagCol).GetString().Trim();
                if (string.Equals(cellTag, res.ServiceTag.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    WriteRow(L, r, res, isNewRow: false);
                    wb.Save();
                    return (true, $"Updated row {r} → Stav '{res.Status}'" + (L.SpecCol > 0 ? $", Spec '{res.Spec}'" : ""));
                }
            }

            if (!allowAppend)
                return (false, $"Service Tag '{res.ServiceTag}' not found in file");

            int newRow = AppendRow(L, res);
            wb.Save();
            return (true, $"Added new row {newRow} for '{res.ServiceTag}' → Stav '{res.Status}'" + (L.SpecCol > 0 ? $", Spec '{res.Spec}'" : ""));
        }
        catch (Exception ex) { return (false, "Error: " + ex.Message); }
    }

    public static (int updated, int added, int skipped, List<string> log) UpdateBatch(string xlsxPath, List<LaptopResult> results, bool allowAppend)
    {
        var log = new List<string>();
        int updated = 0, added = 0, skipped = 0;
        if (!File.Exists(xlsxPath)) { log.Add("File does not exist: " + xlsxPath); return (0, 0, 0, log); }
        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(xlsxPath);
            var L = Detect(wb, out var err);
            if (L == null) { log.Add(err); return (0, 0, 0, log); }

            // Build index: serviceTag -> row
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int r = L.HeaderRow + 1; r <= L.LastDataRow; r++)
            {
                var t = L.Sheet.Cell(r, L.TagCol).GetString().Trim();
                if (!string.IsNullOrEmpty(t) && !index.ContainsKey(t)) index[t] = r;
            }

            // Pass 1: updates. AppendRow inserts above the last data row, which shifts that
            // row down and would make its index entry stale, so no append may run before
            // every existing-row update has been written.
            var toAppend = new List<LaptopResult>();
            foreach (var res in results)
            {
                var st = (res.ServiceTag ?? "").Trim();
                if (string.IsNullOrEmpty(st)) { log.Add("(skipped: empty Service Tag)"); skipped++; continue; }

                if (index.TryGetValue(st, out var row))
                {
                    WriteRow(L, row, res, isNewRow: false);
                    log.Add($"✓ {st}  → updated row {row}, Stav '{res.Status}'");
                    updated++;
                }
                else if (allowAppend)
                {
                    toAppend.Add(res);
                }
                else
                {
                    log.Add($"✗ {st}  — not found in Excel (append disabled)");
                    skipped++;
                }
            }

            // Pass 2: appends. Appended rows sit above the insertion point of later
            // appends, so their row numbers stay valid for a repeated tag in the batch.
            var appended = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var res in toAppend)
            {
                var st = res.ServiceTag.Trim();
                if (appended.TryGetValue(st, out var existingNew))
                {
                    WriteRow(L, existingNew, res, isNewRow: false);
                    log.Add($"✓ {st}  → updated new row {existingNew}, Stav '{res.Status}'");
                    updated++;
                    continue;
                }
                int newRow = AppendRow(L, res);
                appended[st] = newRow;
                log.Add($"+ {st}  → NEW row {newRow}, Stav '{res.Status}'");
                added++;
            }

            if (updated > 0 || added > 0) wb.Save();
            return (updated, added, skipped, log);
        }
        catch (Exception ex) { log.Add("Error: " + ex.Message); return (updated, added, skipped, log); }
    }
}

// =====================================================================
// BATCH IMPORT WINDOW
// =====================================================================
public class BatchImportForm : Form
{
    private readonly TextBox _folder = new() { BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.BodyFont, BorderStyle = BorderStyle.FixedSingle, Width = 700 };
    private readonly TextBox _excel = new() { BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.BodyFont, BorderStyle = BorderStyle.FixedSingle, Width = 700 };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.MonoFont, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly Label _summary = new() { AutoSize = true, ForeColor = Theme.Text, Font = Theme.TitleFont };
    private readonly CheckBox _addNew = new() { Text = "Add new rows for tags not yet in the inventory", AutoSize = true, Checked = true, ForeColor = Theme.Text, Font = Theme.BodyFont, BackColor = Color.Transparent };

    public BatchImportForm()
    {
        Text = "Batch import of results into Excel";
        Width = 920; Height = 660;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;

        var top = new Panel { Dock = DockStyle.Top, Height = 180, BackColor = Theme.Bg, Padding = new Padding(24, 18, 24, 8) };
        var lblF = new Label { Text = "FOLDER WITH RESULTS (result-*.json)", AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.PillFont, Location = new Point(24, 12) };
        _folder.Location = new Point(24, 32);
        _folder.Text = ResultStore.DefaultDir();
        var btnF = ModernButton.Make("Browse…", Theme.CardHover, 90, 26);
        btnF.Location = new Point(740, 32);
        btnF.Click += (s, e) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _folder.Text };
            if (d.ShowDialog() == DialogResult.OK) { _folder.Text = d.SelectedPath; RefreshList(); }
        };

        var lblE = new Label { Text = "EXCEL FILE (.xlsx with your inventory)", AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.PillFont, Location = new Point(24, 72) };
        _excel.Location = new Point(24, 92);
        var btnE = ModernButton.Make("Browse…", Theme.CardHover, 90, 26);
        btnE.Location = new Point(740, 92);
        btnE.Click += (s, e) =>
        {
            using var d = new OpenFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx" };
            if (d.ShowDialog() == DialogResult.OK) _excel.Text = d.FileName;
        };

        _summary.Location = new Point(24, 138);
        top.Controls.Add(lblF); top.Controls.Add(_folder); top.Controls.Add(btnF);
        top.Controls.Add(lblE); top.Controls.Add(_excel); top.Controls.Add(btnE);
        top.Controls.Add(_summary);

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(24, 12, 24, 12) };
        center.Controls.Add(_list);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg, Padding = new Padding(24, 10, 24, 10) };
        var btnRun = ModernButton.Make("Import into Excel", Theme.Success, 200, 36);
        btnRun.Location = new Point(24, 10);
        btnRun.Click += (s, e) => Run();
        var btnRefresh = ModernButton.Make("Reload", Theme.CardHover, 120, 36);
        btnRefresh.Location = new Point(236, 10);
        btnRefresh.Click += (s, e) => RefreshList();
        _addNew.Location = new Point(372, 18);
        bottom.Controls.Add(btnRun); bottom.Controls.Add(btnRefresh); bottom.Controls.Add(_addNew);

        Controls.Add(center); Controls.Add(top); Controls.Add(bottom);
        Load += (s, e) => RefreshList();
    }

    private List<LaptopResult> _loaded = new();

    private void RefreshList()
    {
        _list.Items.Clear();
        _loaded.Clear();
        var items = ResultStore.LoadAll(_folder.Text);
        foreach (var (file, r, err) in items)
        {
            if (err != null) { _list.Items.Add($"✗  {Path.GetFileName(file)}  — {err}"); continue; }
            _loaded.Add(r);
            var stamp = r.TestedAt == default ? "" : r.TestedAt.ToString("yyyy-MM-dd HH:mm");
            _list.Items.Add($"{r.ServiceTag,-10}  {r.Model,-25}  {r.Status,-22}  {stamp}  ({r.TestsPass}/{r.TestsTotal} OK)");
        }
        _summary.Text = $"Found {_loaded.Count} results in folder";
    }

    private void Run()
    {
        if (string.IsNullOrWhiteSpace(_excel.Text) || !File.Exists(_excel.Text))
        {
            MessageBox.Show("Select Excel file.", "Missing Excel"); return;
        }
        if (_loaded.Count == 0)
        {
            MessageBox.Show("No results to import.", "Empty"); return;
        }
        var (updated, added, skipped, log) = ExcelExport.UpdateBatch(_excel.Text, _loaded, _addNew.Checked);
        _list.Items.Clear();
        foreach (var l in log) _list.Items.Add(l);
        _summary.Text = $"Done: {updated} updated, {added} added, {skipped} skipped";
        MessageBox.Show($"Updated: {updated}\nAdded new rows: {added}\nSkipped: {skipped}", "Import done");
    }
}

// =====================================================================
// ROUNDED BUTTON — fully custom-painted, rounded corners, hover effect
// =====================================================================
public class RoundedButton : Button
{
    public Color FillColor { get; set; } = Theme.Accent;
    public Color HoverColor { get; set; } = Theme.AccentHover;
    public int CornerRadius { get; set; } = 8;
    public bool Outline { get; set; } = false;
    private bool _hover;
    private bool _pressed;

    public RoundedButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        Font = Theme.ButtonFont;
        Height = 36;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _pressed = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _pressed = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs pe)
    {
        var g = pe.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // CRITICAL: Fill the entire button rect with parent's background color first,
        // so the rounded button's corners are opaque (don't show whatever is behind through transparency)
        Color bg = Parent?.BackColor ?? Theme.Card;
        if (bg == Color.Transparent || bg.A < 255) bg = Theme.Card;
        using (var bgBrush = new SolidBrush(bg))
            g.FillRectangle(bgBrush, 0, 0, Width, Height);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Shape.Rounded(r, CornerRadius);
        var color = !Enabled ? Color.FromArgb(180, FillColor) : _hover ? HoverColor : FillColor;
        if (_pressed) color = ControlPaint.Dark(color, 0.05f);
        if (Outline)
        {
            using var pen = new Pen(color, 1.5f);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Text, Font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
            TextRenderer.DrawText(g, Text, Font, r, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Skip — we paint everything in OnPaint
    }
}

public static class ModernButton
{
    public static RoundedButton Make(string text, Color color, int width = 120, int height = 36)
    {
        return new RoundedButton
        {
            Text = text,
            Width = width,
            Height = height,
            FillColor = color,
            HoverColor = ControlPaint.Dark(color, 0.08f),
            ForeColor = Color.White
        };
    }

    public static RoundedButton MakeOutline(string text, Color color, int width = 120, int height = 36)
    {
        return new RoundedButton
        {
            Text = text,
            Width = width,
            Height = height,
            FillColor = color,
            HoverColor = ControlPaint.Dark(color, 0.08f),
            ForeColor = color,
            Outline = true
        };
    }
}
