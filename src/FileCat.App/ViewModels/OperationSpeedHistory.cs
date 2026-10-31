using Avalonia;

namespace FileCat.App.ViewModels;

/// <summary>A bounded, elapsed-time series of actual bytes or settled items per second.</summary>
internal sealed class OperationSpeedHistory
{
    internal const double WindowSeconds = 90;
    private readonly List<Point> _samples = [];
    private TimeSpan? _lastAt;
    private long _lastUnits;
    private bool _everActive;

    internal IReadOnlyList<Point> Samples => _samples;
    // Remember real activity so a later zero-rate stretch remains meaningful while at least two samples remain.
    internal bool HasActivity => _everActive;
    internal bool HasTrend => HasActivity && _samples.Count >= 2;
    internal double Peak => _samples.Count == 0 ? 0 : _samples.Max(point => point.Y);
    internal double Current => _samples.Count == 0 ? 0 : _samples[^1].Y;

    internal bool Sample(TimeSpan at, long units, bool running)
    {
        if (_lastAt is not { } last)
        {
            _lastAt = at;
            _lastUnits = units;
            return false;
        }
        double seconds = (at - last).TotalSeconds;
        if (seconds < 1) return false;
        double rate = running ? Math.Max(0, units - _lastUnits) / seconds : 0;
        _everActive |= rate > 0;
        _samples.Add(new Point(at.TotalSeconds, rate));
        _lastAt = at;
        _lastUnits = units;
        double oldest = at.TotalSeconds - WindowSeconds;
        while (_samples.Count > 0 && _samples[0].X < oldest) _samples.RemoveAt(0);
        return true;
    }
}
