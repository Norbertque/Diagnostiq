using System.Windows.Threading;

namespace Diagnostiq;

/// <summary>
/// Notices when the UI thread stops responding (a driver or COM call stuck on it) and writes it to
/// errors.log with the screen that was showing, so a freeze a user reports leaves a trace.
/// </summary>
internal static class UiWatchdog
{
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(5);
    private static long _answeredAt = Environment.TickCount64;
    private static volatile bool _pending;

    /// <summary>The screen or step showing now; set by the windows as they change.</summary>
    public static volatile string Where = "starting up";

    public static void Start(Dispatcher dispatcher)
    {
        var thread = new Thread(() =>
        {
            bool reported = false;
            while (!dispatcher.HasShutdownStarted)
            {
                if (!_pending)
                {
                    _pending = true;
                    dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
                    {
                        Volatile.Write(ref _answeredAt, Environment.TickCount64);
                        _pending = false;
                    });
                }
                Thread.Sleep(1000);
                var silent = TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref _answeredAt));
                if (silent >= Silence && !reported)
                {
                    reported = true;
                    App.LogError(new TimeoutException($"The window stopped responding for over {Silence.TotalSeconds:0} s (showing: {Where})."));
                }
                else if (silent < Silence && reported)
                {
                    reported = false;
                    App.LogError(new TimeoutException($"The window is responding again (showing: {Where})."));
                }
            }
        })
        { IsBackground = true, Name = "UI watchdog" };
        thread.Start();
    }
}
