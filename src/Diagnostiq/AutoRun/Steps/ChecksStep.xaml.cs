using Diagnostiq.Core;
using Diagnostiq.Core.Network;
using Diagnostiq.Core.Testing;

namespace Diagnostiq.AutoRun.Steps;

/// <summary>Internet reachability and a fresh Wi-Fi scan: a few seconds, no input needed.</summary>
public partial class ChecksStep : StepView
{
    public ChecksStep() => InitializeComponent();

    public override string Id => TestIds.Network;
    public override string Title => "Wi-Fi and internet";   // the same name as its result and the Manual test card
    public override StepMode Mode => StepMode.Automatic;

    protected override async Task OnRunAsync(CancellationToken ct)
    {
        var adapters = Ctx.Snapshot.Network.Value ?? [];
        var gateway = adapters.FirstOrDefault(a => a.IsConnected && a.Gateway is not null)?.Gateway;
        bool ethernet = adapters.Any(a => a.Kind == AdapterKind.Ethernet && a.IsConnected);

        var pingTask = PingTest.RunAsync(gateway, ct);
        var wifiTask = Task.Run(async () =>
        {
            try { return await WifiProbe.ScanAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Ctx.Snapshot.Wifi.Value; }   // scan refused: use startup data
        }, ct);

        var ping = await pingTask;
        long? rtt = ping.Targets.Where(t => t.Reachable && t.Label != "Gateway").Min(t => t.RoundTripMs);
        InternetIcon.State = ping.InternetReachable ? CheckState.Pass : ping.GatewayReachable ? CheckState.Warn : CheckState.Fail;
        InternetText.Text = ping.InternetReachable ? $"Online{(rtt is { } ms ? $", {ms} ms" : "")}."
                          : ping.GatewayReachable ? "Connected to the router, but not to the internet."
                          : "Not connected.";

        var wifi = await wifiTask;
        WifiIcon.State = wifi switch
        {
            { AdapterPresent: false } or null => CheckState.Fail,
            { RadioOn: false } => CheckState.Warn,
            { NetworksVisible: > 0 } => CheckState.Pass,
            { NamesHidden: true } => CheckState.Warn,
            _ => CheckState.Fail,
        };
        WifiText.Text = wifi switch
        {
            { AdapterPresent: false } or null => "No Wi-Fi adapter found.",
            { RadioOn: false } => "Wi-Fi is switched off.",
            { NetworksVisible: > 0 } w => $"{w.NetworksVisible} network{(w.NetworksVisible == 1 ? "" : "s")} in range" +
                                           (w.BandsSeen.Count > 0 ? $" on {string.Join(" and ", w.BandsSeen)}." : "."),
            { NamesHidden: true } => "Windows hides network names (Location access is off).",
            _ => "No networks found.",
        };

        Ctx.Run.Ping = ping;
        Ctx.Run.WifiScan = wifi;
        Ctx.Run.Record(Evaluate.Network(ping, wifi, ethernet));
        await Task.Delay(TimeSpan.FromSeconds(2), ct);   // long enough to read the result
    }
}
