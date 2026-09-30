using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Diagnostiq.Interop;

/// <param name="Scan">Hardware scan code (layout-independent: identifies the physical key).</param>
/// <param name="Extended">E0-prefixed key (right Ctrl/Alt, arrows, Delete, …).</param>
public readonly record struct KeyEvent(int Vk, int Scan, bool Extended, bool Down);

/// <summary>
/// System-wide low-level keyboard hook (WH_KEYBOARD_LL) for the keyboard test. While it is
/// installed every key press is reported and swallowed, so Esc, the Windows key, Alt+Tab and
/// Alt+F4 are tested instead of acted on. Ctrl+Alt+Del and Fn never reach applications.
/// The callback only queues the event: Windows drops hooks that answer slowly.
/// </summary>
public sealed unsafe partial class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int LlkhfExtended = 0x01, LlkhfInjected = 0x10, LlkhfUp = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public int VkCode, ScanCode, Flags, Time;
        public nint ExtraInfo;
    }

    private static KeyboardHook? _active;
    private readonly Dispatcher _dispatcher;
    private nint _hook;

    public event Action<KeyEvent>? Key;

    public KeyboardHook()
    {
        ReleaseAll();   // only ever one
        _dispatcher = Dispatcher.CurrentDispatcher;
        _active = this;
        _hook = SetWindowsHookExW(WhKeyboardLl, &Callback, GetModuleHandleW(null), 0);
        if (_hook == 0) { _active = null; throw new InvalidOperationException($"Keyboard hook failed (error {Marshal.GetLastPInvokeError()})."); }
    }

    /// <summary>Crash safety: the app's unhandled-exception handler calls this so keys never stay captured.</summary>
    public static void ReleaseAll() => _active?.Dispose();

    [UnmanagedCallersOnly]
    private static nint Callback(int code, nint wParam, nint lParam)
    {
        var hook = _active;
        if (code < 0 || hook is null) return CallNextHookEx(0, code, wParam, lParam);

        var k = *(KbdLlHookStruct*)lParam;
        // Ignore synthetic input (remote tools, on-screen keyboards) and the fake Ctrl that AltGr sends (scan 0x21D).
        if ((k.Flags & LlkhfInjected) == 0 && k.ScanCode <= 0xFF)
        {
            var e = new KeyEvent(k.VkCode, k.ScanCode, (k.Flags & LlkhfExtended) != 0, (k.Flags & LlkhfUp) == 0);
            hook._dispatcher.BeginInvoke(() => hook.Key?.Invoke(e));
        }
        return 1;   // swallowed: nothing else sees the key
    }

    public void Dispose()
    {
        if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
        if (_active == this) _active = null;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, delegate* unmanaged<int, nint, nint, nint> lpfn, nint hMod, int threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int code, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
