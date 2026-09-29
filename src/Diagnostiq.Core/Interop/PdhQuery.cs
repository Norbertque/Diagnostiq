using System.Runtime.InteropServices;

namespace Diagnostiq.Core.Interop;

/// <summary>
/// Minimal PDH query. Counter paths are English (e.g. <c>\Processor Information(_Total)\% Processor Performance</c>)
/// and work on any Windows display language. Rate counters need two <see cref="Collect"/> calls
/// before the first value is valid.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private IntPtr _query;

    public PdhQuery()
    {
        if (Native.PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
            throw new InvalidOperationException("PDH query could not be opened.");
    }

    /// <summary>Returns null when the counter doesn't exist on this machine.</summary>
    public IntPtr? Add(string englishPath) =>
        Native.PdhAddEnglishCounter(_query, englishPath, IntPtr.Zero, out var counter) == 0 ? counter : null;

    public bool Collect() => Native.PdhCollectQueryData(_query) == 0;

    public static double? Value(IntPtr counter) =>
        Native.PdhGetFormattedCounterValue(counter, Native.PdhFmtDouble, out _, out var v) == 0 && v.CStatus == 0
            ? v.DoubleValue : null;

    /// <summary>All instances of a wildcard counter, e.g. <c>\Thermal Zone Information(*)\...</c>.</summary>
    public static List<(string Instance, double Value)> Values(IntPtr counter)
    {
        var result = new List<(string, double)>();
        uint size = 0;
        if (Native.PdhGetFormattedCounterArray(counter, Native.PdhFmtDouble, ref size, out _, IntPtr.Zero) != Native.PdhMoreData)
            return result;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (Native.PdhGetFormattedCounterArray(counter, Native.PdhFmtDouble, ref size, out var count, buffer) != 0)
                return result;
            int stride = Marshal.SizeOf<Native.PdhFmtCounterValueItem>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<Native.PdhFmtCounterValueItem>(buffer + i * stride);
                if (item.Value.CStatus == 0)
                    result.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value.DoubleValue));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) { Native.PdhCloseQuery(_query); _query = IntPtr.Zero; }
    }
}
