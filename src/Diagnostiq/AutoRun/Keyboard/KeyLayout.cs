using System.Runtime.InteropServices;
using System.Text;
using Diagnostiq.Interop;

namespace Diagnostiq.AutoRun.Keyboard;

/// <param name="Scan">Set-1 scan code; -1 = empty space.</param>
/// <param name="Width">In key units (1 = a letter key).</param>
/// <param name="Optional">Not on every laptop (Menu, right Ctrl, Print Screen): doesn't block "all keys work".</param>
/// <param name="Untestable">Fn: handled by the keyboard firmware, never reaches Windows.</param>
/// <param name="Half">Half-height key, stacked with the next one (the up/down arrows).</param>
public sealed record KeyDef(int Scan, bool Ext = false, double Width = 1, bool Optional = false, bool Untestable = false, bool Half = false)
{
    public bool IsGap => Scan < 0;
    public (int Scan, bool Ext) Id => (Scan, Ext);
}

/// <summary>
/// Physical laptop keyboard (ISO or ANSI) by scan code, so it matches the hardware whatever
/// language is set; labels come from the active Windows keyboard layout (Czech shows ě š č …).
/// </summary>
public static partial class KeyLayout
{
    private static KeyDef K(int scan, double w = 1, bool opt = false) => new(scan, false, w, opt);
    private static KeyDef E(int scan, double w = 1, bool opt = false, bool half = false) => new(scan, true, w, opt, Half: half);

    public const int IsoExtraKey = 0x56;   // the key left of Z that only ISO boards have

    public static IReadOnlyList<IReadOnlyList<KeyDef>> Rows(bool iso)
    {
        var fRow = new List<KeyDef> { K(0x01) };
        fRow.AddRange(Enumerable.Range(0x3B, 10).Select(s => K(s)));   // F1–F10
        fRow.AddRange([K(0x57), K(0x58), E(0x37, opt: true), E(0x53)]); // F11, F12, Print Screen, Delete

        var numbers = new List<KeyDef> { K(0x29) };
        numbers.AddRange(Enumerable.Range(0x02, 12).Select(s => K(s)));  // 1 … =
        numbers.Add(K(0x0E, 2));                                         // Backspace

        var top = new List<KeyDef> { K(0x0F, 1.5) };
        top.AddRange(Enumerable.Range(0x10, 12).Select(s => K(s)));      // Q … ]
        top.Add(iso ? K(0x1C, 1.5) : K(0x2B, 1.5));                      // ISO: upper half of Enter; ANSI: backslash

        var home = new List<KeyDef> { K(0x3A, 1.75) };
        home.AddRange(Enumerable.Range(0x1E, 11).Select(s => K(s)));     // A … '
        home.AddRange(iso ? [K(0x2B), K(0x1C, 1.25)] : [K(0x1C, 2.25)]);

        var bottom = new List<KeyDef> { K(0x2A, iso ? 1.25 : 2.25) };
        if (iso) bottom.Add(K(IsoExtraKey));
        bottom.AddRange(Enumerable.Range(0x2C, 10).Select(s => K(s)));   // Z … /
        bottom.Add(K(0x36, 2.75));

        List<KeyDef> space =
        [
            K(0x1D, 1.25), new KeyDef(0, Untestable: true), E(0x5B, 1.25), K(0x38, 1.25), K(0x39, 4.25),
            E(0x38), E(0x5D, opt: true), E(0x1D, opt: true),
            E(0x4B), E(0x48, half: true), E(0x50, half: true), E(0x4D),
        ];
        return [fRow, numbers, top, home, bottom, space];
    }

    /// <summary>
    /// US, Japanese, Korean and Chinese layouts usually sit on ANSI-shaped keyboards; the rest of
    /// the world uses ISO. Uses the layout id (KLID, "00000405" = Czech), not the input language,
    /// which is often English on a machine with a local keyboard.
    /// </summary>
    public static bool DefaultIso()
    {
        var klid = new StringBuilder(9);
        int layout = GetKeyboardLayoutNameW(klid) && int.TryParse(klid.ToString(), System.Globalization.NumberStyles.HexNumber, null, out var id)
            ? id & 0xFFFF
            : (int)(GetKeyboardLayout(0) & 0xFFFF);
        return layout is not (0x0409 or 0x0411 or 0x0412 or 0x0804 or 0x0404 or 0x0C04);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardLayoutNameW(StringBuilder klid);

    public static string Label(KeyDef k)
    {
        if (k.Untestable) return "Fn";
        if (Fixed(k.Scan, k.Ext) is { } name) return name;

        nint hkl = GetKeyboardLayout(0);
        uint vk = MapVirtualKeyExW((uint)(k.Scan | (k.Ext ? 0xE000 : 0)), 3 /* VSC_TO_VK_EX */, hkl);
        var state = new byte[256];
        var buf = new StringBuilder(8);
        int n = ToUnicodeEx(vk, (uint)k.Scan, state, buf, buf.Capacity, 4 /* don't change keyboard state */, hkl);
        // n < 0: dead key (´ ˇ on Czech); its character is still in the buffer.
        return n != 0 && buf.Length > 0 && !char.IsControl(buf[0]) ? buf.ToString(0, 1).ToUpperInvariant() : $"{k.Scan:X2}";
    }

    private static string? Fixed(int scan, bool ext) => (scan, ext) switch
    {
        (0x01, _) => "Esc",
        (>= 0x3B and <= 0x44, false) => $"F{scan - 0x3A}",
        (0x57, false) => "F11",
        (0x58, false) => "F12",
        (0x37, true) => "PrtSc",
        (0x53, true) => "Del",
        (0x0E, _) => "Backspace",
        (0x0F, _) => "Tab",
        (0x3A, _) => "Caps Lock",
        (0x1C, _) => "Enter",
        (0x2A, _) or (0x36, _) => "Shift",
        (0x1D, _) => "Ctrl",
        (0x5B, true) => "Win",
        (0x38, false) => "Alt",
        (0x38, true) => "Alt Gr",
        (0x39, _) => "Space",
        (0x5D, true) => "Menu",
        (0x4B, true) => "←",
        (0x48, true) => "↑",
        (0x50, true) => "↓",
        (0x4D, true) => "→",
        _ => null,
    };

    /// <summary>Readable names for keys outside the main block (media, numpad, Home/End, Copilot…).</summary>
    public static string OtherKeyName(KeyEvent e) => e.Vk switch
    {
        0x86 => "Copilot",
        >= 0x70 and <= 0x87 => $"F{e.Vk - 0x6F}",
        0x24 => "Home", 0x23 => "End", 0x21 => "Page Up", 0x22 => "Page Down", 0x2D => "Insert",
        0x13 => "Pause", 0x91 => "Scroll Lock", 0x90 => "Num Lock", 0x2C => "Print Screen", 0x5C => "Right Win",
        0xAD => "Mute", 0xAE => "Volume down", 0xAF => "Volume up",
        0xB3 => "Play/Pause", 0xB0 => "Next track", 0xB1 => "Previous track", 0xB2 => "Stop",
        0xB7 => "Calculator", 0xB6 => "This PC", 0xB4 => "Mail", 0xAC => "Browser home", 0xAA => "Search",
        0xA6 => "Browser back", 0xA7 => "Browser forward", 0x5F => "Sleep",
        >= 0x60 and <= 0x69 => $"Num {e.Vk - 0x60}",
        0x6A => "Num *", 0x6B => "Num +", 0x6D => "Num −", 0x6E => "Num .", 0x6F => "Num /",
        0x0D when e.Extended => "Num Enter",
        _ => $"Key {e.Vk:X2}",
    };

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint threadId);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyExW(uint code, uint mapType, nint hkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint vk, uint scan, byte[] keyState, StringBuilder buf, int bufSize, uint flags, nint hkl);
}
