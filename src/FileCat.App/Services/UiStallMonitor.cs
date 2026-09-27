using System.Diagnostics;
using Avalonia.Threading;
using FileCat.Core.Diagnostics;

namespace FileCat.App.Services;

/// <summary>
/// Detects UI-thread stalls (plan §19.2 diagnostics): a timer that should tick every 250 ms measures how late it
/// runs. Stalls are traced through the EventSource and summarized in the diagnostics bundle; long ones are logged.
/// </summary>
public static class UiStallMonitor
{
    private const int IntervalMs = 250;
    private static DispatcherTimer? _timer;
    private static long _last;
    private static int _count;
    private static double _worst;

    public static int StallCount => Volatile.Read(ref _count);
    public static double WorstStallMs => Volatile.Read(ref _worst);

    public static void Start()
    {
        if (_timer is not null) return;
        _last = Stopwatch.GetTimestamp();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(IntervalMs), DispatcherPriority.Normal, (_, _) => Tick());
        _timer.Start();
    }

    private static void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        double late = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds - IntervalMs;
        _last = now;
        // Suspend/resume shows up as one enormous gap: not a stall.
        if (late < 200 || late > 60_000) return;
        Interlocked.Increment(ref _count);
        if (late > _worst) _worst = late;
        FileCatEventSource.Log.UiStall(late);
        if (late >= 1000) AppLog.Warn($"The UI thread was unresponsive for {late:0} ms.");
    }

    public static string Describe() =>
        StallCount == 0 ? "UI stalls (>200 ms): none" : $"UI stalls (>200 ms): {StallCount}, worst {WorstStallMs:0} ms";
}
