using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;

namespace FileCat.ArchiveQualificationHost;

/// <summary>
/// Plan §11.8 UI budget, measured in the real interface (this host only, opted in with <c>--ui-budget REPORT.json</c>): a
/// 10 ms dispatcher timer on the UI thread notes every time the thread was unavailable for longer than the 50 ms budget
/// (beyond the timer's own period), with the UTC time the gap ended and the garbage-collection pause time meanwhile. A
/// driving script attributes each gap to what it was doing then (idle, a large background pack, a format scan). The report
/// is rewritten atomically every second from a background thread, and once more when the process exits.
/// </summary>
internal sealed class UiBudgetProbe
{
    internal const double PeriodMs = 10, BudgetMs = 50;
    private const int MaximumGaps = 5000;
    private static readonly double[] s_bounds = [10, 20, 50, 100, 200, 500];
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<Gap> _gaps = [];
    private readonly long[] _histogram = new long[s_bounds.Length + 1];
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private DispatcherTimer? _timer;
    private Timer? _writer;
    private long _last, _ticks, _over;
    private double _worst;
    private TimeSpan _pauseAtLast;

    private sealed record Gap(DateTime EndUtc, double Ms, double GcPauseMs);

    private UiBudgetProbe(string path) => _path = path;

    /// <summary>Starts the probe on the UI thread (call there, after Avalonia's setup).</summary>
    internal static UiBudgetProbe Start(string path)
    {
        var probe = new UiBudgetProbe(Path.GetFullPath(path));
        // Windows rounds timers to 15.6 ms unless a process asks for 1 ms; the host does, so ticks come every 10 ms.
        if (OperatingSystem.IsWindows()) _ = TimeBeginPeriod(1);
        probe._last = Stopwatch.GetTimestamp();
        probe._pauseAtLast = GC.GetTotalPauseDuration();
        probe._timer = new DispatcherTimer(TimeSpan.FromMilliseconds(PeriodMs), DispatcherPriority.Normal, (_, _) => probe.Tick());
        probe._timer.Start();
        probe._writer = new Timer(_ => probe.Write(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => probe.Write();
        return probe;
    }

    private void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        double gap = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds - PeriodMs;
        var pause = GC.GetTotalPauseDuration();
        double gcMs = (pause - _pauseAtLast).TotalMilliseconds;
        _last = now; _pauseAtLast = pause;
        lock (_gate)
        {
            _ticks++;
            int bucket = 0;
            while (bucket < s_bounds.Length && gap >= s_bounds[bucket]) bucket++;
            _histogram[bucket]++;
            if (gap > _worst) _worst = gap;
            if (gap <= BudgetMs) return;
            _over++;
            if (_gaps.Count < MaximumGaps) _gaps.Add(new Gap(DateTime.UtcNow, Math.Round(gap, 1), Math.Round(gcMs, 1)));
        }
    }

    private void Write()
    {
        object report;
        lock (_gate)
        {
            report = new
            {
                PeriodMs, BudgetMs, StartedUtc = _startedUtc, WrittenUtc = DateTime.UtcNow, Ticks = _ticks, OverBudget = _over,
                WorstMs = Math.Round(_worst, 1),
                Histogram = s_bounds.Select((bound, i) => new { Below = bound, Count = _histogram[i] })
                    .Append(new { Below = double.PositiveInfinity, Count = _histogram[^1] }).ToArray(),
                Gaps = _gaps.ToArray(),
            };
        }
        try
        {
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException) { /* The next second writes again. */ }
        catch (UnauthorizedAccessException) { }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);
}
