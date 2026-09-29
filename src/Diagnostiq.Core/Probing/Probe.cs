using System.Management;
using System.Runtime.InteropServices;

namespace Diagnostiq.Core.Probing;

public static class Probe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    /// <summary>True when the process runs elevated (TPM, raw disk, some WMI classes need it).</summary>
    public static bool IsAdmin => Environment.IsPrivilegedProcess;

    /// <summary>
    /// Runs a blocking hardware query on the thread pool with a timeout. A query that
    /// hangs (common with broken WMI providers on old machines) is abandoned, not awaited,
    /// so one bad source can't stall startup. Returning null means "not present".
    /// </summary>
    public static async Task<ProbeResult<T>> RunAsync<T>(Func<T?> query, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var limit = timeout ?? DefaultTimeout;
        var work = Task.Run(() => Classify(query), CancellationToken.None);
        try
        {
            return await work.WaitAsync(limit, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return ProbeResult<T>.TimedOut(limit);
        }
    }

    private static ProbeResult<T> Classify<T>(Func<T?> query)
    {
        try
        {
            var value = query();
            return value is null ? ProbeResult<T>.NotAvailable() : ProbeResult<T>.Ok(value);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            return ProbeResult<T>.NeedsAdmin();
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass or ManagementStatus.NotSupported)
        {
            return ProbeResult<T>.NotAvailable(ex.Message.Trim());
        }
        catch (UnauthorizedAccessException)
        {
            return ProbeResult<T>.NeedsAdmin();
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070005) // E_ACCESSDENIED
        {
            return ProbeResult<T>.NeedsAdmin();
        }
        catch (Exception ex)
        {
            return ProbeResult<T>.Error(ex.Message);
        }
    }
}
