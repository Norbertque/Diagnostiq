namespace Diagnostiq.Core.Probing;

public enum ProbeStatus
{
    Ok,
    /// <summary>The hardware or data source doesn't exist on this machine.</summary>
    NotAvailable,
    /// <summary>The query needs an elevated process.</summary>
    NeedsAdmin,
    /// <summary>The source didn't answer in time (hung WMI provider, slow firmware).</summary>
    TimedOut,
    Error,
}

/// <summary>Outcome of one hardware query. Never throws; the UI renders every status.</summary>
public sealed record ProbeResult<T>(ProbeStatus Status, T? Value, string? Message = null)
{
    public bool IsOk => Status == ProbeStatus.Ok && Value is not null;

    public static ProbeResult<T> Ok(T value) => new(ProbeStatus.Ok, value);
    public static ProbeResult<T> NotAvailable(string? message = null) => new(ProbeStatus.NotAvailable, default, message);
    public static ProbeResult<T> NeedsAdmin(string? message = null) => new(ProbeStatus.NeedsAdmin, default, message ?? "Needs admin");
    public static ProbeResult<T> TimedOut(TimeSpan after) => new(ProbeStatus.TimedOut, default, $"No answer within {after.TotalSeconds:0} s");
    public static ProbeResult<T> Error(string message) => new(ProbeStatus.Error, default, message);
}
