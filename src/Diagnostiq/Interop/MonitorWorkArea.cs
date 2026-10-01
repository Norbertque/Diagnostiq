using System.Runtime.InteropServices;
using System.Windows;

namespace Diagnostiq.Interop;

/// <summary>Work area (screen minus taskbar) of the monitor a window is on, in physical pixels.</summary>
internal static partial class MonitorWorkArea
{
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    /// <summary>Null when Windows can't say (the caller then keeps the window as it is).</summary>
    public static Int32Rect? Of(nint hwnd)
    {
        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == 0) return null;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) return null;
        var w = info.Work;
        return new Int32Rect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top);
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
}
