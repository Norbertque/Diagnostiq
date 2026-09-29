using System.Net.NetworkInformation;

namespace Diagnostiq.Core.Network;

public sealed record PingTarget(string Label, string Host, bool Reachable, long? RoundTripMs, string? Error);

public sealed record PingResult(IReadOnlyList<PingTarget> Targets)
{
    /// <summary>Internet works if at least one public target answers (a single blocked host isn't a fault).</summary>
    public bool InternetReachable => Targets.Where(t => t.Label != "Gateway").Any(t => t.Reachable);
    public bool GatewayReachable => Targets.FirstOrDefault(t => t.Label == "Gateway")?.Reachable ?? false;
}

public static class PingTest
{
    public static async Task<PingResult> RunAsync(string? gateway, CancellationToken ct = default)
    {
        var targets = new List<(string Label, string? Host)>
        {
            ("Gateway", gateway), ("Google DNS", "8.8.8.8"), ("Cloudflare", "1.1.1.1"), ("google.com", "google.com"),
        };
        var tasks = targets.Where(t => t.Host is not null).Select(t => PingOne(t.Label, t.Host!, ct));
        return new PingResult(await Task.WhenAll(tasks).ConfigureAwait(false));
    }

    private static async Task<PingTarget> PingOne(string label, string host, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            // Best of three, so one dropped packet on Wi-Fi doesn't fail the check.
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();
                var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(1500), cancellationToken: ct).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success) return new PingTarget(label, host, true, reply.RoundtripTime, null);
            }
            return new PingTarget(label, host, false, null, "No reply");
        }
        catch (PingException ex) { return new PingTarget(label, host, false, null, ex.InnerException?.Message ?? ex.Message); }
    }
}
