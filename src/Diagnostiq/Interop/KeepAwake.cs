using System.Runtime.InteropServices;

namespace Diagnostiq.Interop;

/// <summary>
/// Keeps the screen on and the laptop awake while tests run (nobody touches it during a
/// 15-minute stress test, and sleep would end it). Must be released on the same thread.
/// </summary>
public static partial class KeepAwake
{
    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001, EsDisplayRequired = 0x00000002;

    public static void Begin() => SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);

    public static void End() => SetThreadExecutionState(EsContinuous);

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);
}
