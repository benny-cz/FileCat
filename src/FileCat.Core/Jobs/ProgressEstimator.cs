namespace FileCat.Core.Jobs;

/// <summary>A job's progress as its display samples it.</summary>
/// <param name="Now">Time on a clock that only moves forward.</param>
/// <param name="WorkDone">Work done in bytes (<see cref="Job.WorkBytesDone"/>).</param>
/// <param name="WorkTotal">All work in bytes, as far as known (<see cref="Job.WorkBytesTotal"/>).</param>
/// <param name="ItemsDone">Items finished, whatever their outcome.</param>
/// <param name="ItemsTotal">Items to do, as far as known.</param>
/// <param name="TotalsFinal">Whether the totals are known in full.</param>
/// <param name="Advancing">Whether the job is running now (not queued, paused, or waiting for an answer).</param>
public readonly record struct ProgressSample(TimeSpan Now, long WorkDone, long WorkTotal, long ItemsDone, long ItemsTotal, bool TotalsFinal, bool Advancing);

/// <summary>What the progress shown means now, when no time left is given.</summary>
public enum ProgressNote
{
    None,
    /// <summary>Too little measured yet to say how long it takes.</summary>
    Measuring,
    /// <summary>The work is still being counted.</summary>
    Counting,
    /// <summary>Nothing has moved for a while.</summary>
    Stalled,
    /// <summary>All counted work is done; the job is finishing (times, folders, its journal).</summary>
    Finishing,
}

/// <summary>What to show for a job's progress.</summary>
/// <param name="Fraction">0 to 1: below 1 until the job has ended, and never less than shown before.</param>
/// <param name="Likely">The time left as the job has gone so far; null when none is claimed.</param>
/// <param name="Pessimistic">A time left the job exceeds only 1 time in 10; at least <paramref name="Likely"/>.</param>
public readonly record struct ProgressEstimate(double Fraction, TimeSpan? Likely, TimeSpan? Pessimistic, ProgressNote Note);

/// <summary>
/// Progress and time left for a job (release issue I26), honest by construction. The work is modelled as time per
/// megabyte plus time per item — a folder of small files costs by the file, a large file by the byte — fitted by
/// weighted least squares to what the job has done, once with a short memory (follows a change of pace) and once with a
/// long one (steady). The time left is a range: the likely value, and a pessimistic one that adds the model's own
/// uncertainty, which grows where the work left differs from the work measured. The fraction weighs the work by that
/// model, so a folder of small files moves the bar as steadily as a large file. Shown values count down smoothly, rise
/// more slowly than they fall, and are withheld while measuring, counting, stalled, or finishing.
/// </summary>
public sealed class ProgressEstimator
{
    private static readonly TimeSpan SampleLength = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MeasureFirst = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(6);
    private const double Megabyte = 1_000_000;
    private const double Z90 = 1.2816; // one-sided 90%
    private const double MaxFraction = 0.99;

    private Fit _short = new(15), _long = new(120);
    private TimeSpan? _sampleStart;
    private long _sampleWork, _sampleItems;
    private double _moving, _sampleSeconds;
    private int _samples, _phaseShift;
    private TimeSpan _lastProgress;
    private long _lastWork = -1, _lastItems = -1;
    private double _fraction;
    private double? _shownLikely, _shownPessimistic;
    private TimeSpan _shownAt;

    /// <summary>Takes the latest sample and says what to show.</summary>
    public ProgressEstimate Update(ProgressSample s)
    {
        if (s.WorkDone != _lastWork || s.ItemsDone != _lastItems)
        {
            _lastWork = s.WorkDone;
            _lastItems = s.ItemsDone;
            _lastProgress = s.Now;
        }
        if (!s.Advancing)
        {
            // Paused or waiting: that time is not the job's pace, and what was shown before is stale afterwards.
            _sampleStart = null;
            _lastProgress = s.Now;
            _shownLikely = _shownPessimistic = null;
            return new(_fraction = Math.Max(_fraction, Math.Min(MaxFraction, RawFraction(s))), null, null, ProgressNote.None);
        }
        Measure(s);
        _fraction = Math.Max(_fraction, Math.Min(MaxFraction, RawFraction(s)));

        long workLeft = Math.Max(0, s.WorkTotal - s.WorkDone), itemsLeft = Math.Max(0, s.ItemsTotal - s.ItemsDone);
        var note = !s.TotalsFinal ? ProgressNote.Counting
            : workLeft == 0 && itemsLeft == 0 && (s.WorkTotal > 0 || s.ItemsTotal > 0) ? ProgressNote.Finishing
            : (s.Now - _lastProgress).TotalSeconds > Math.Max(StallAfter.TotalSeconds, 4 * _sampleSeconds) ? ProgressNote.Stalled
            : _moving < MeasureFirst.TotalSeconds || _samples < 3 ? ProgressNote.Measuring
            : ProgressNote.None;
        if (note != ProgressNote.None || !_long.TrySolve(out _))
        {
            _shownLikely = _shownPessimistic = null;
            return new(_fraction, null, null, note);
        }
        var (likely, pessimistic) = Predict(workLeft / Megabyte, itemsLeft);
        Show(s.Now, likely, pessimistic);
        return new(_fraction, TimeSpan.FromSeconds(Math.Max(1, _shownLikely!.Value)), TimeSpan.FromSeconds(Math.Max(1, _shownPessimistic!.Value)), ProgressNote.None);
    }

    /// <summary>Gathers samples of at least <see cref="SampleLength"/> with some progress in each.</summary>
    private void Measure(ProgressSample s)
    {
        if (_sampleStart is not { } start || s.WorkDone < _sampleWork || s.ItemsDone < _sampleItems)
        {
            // The first sample, after a pause, or work taken back (a failed file's bytes): start afresh.
            _sampleStart = s.Now;
            _sampleWork = s.WorkDone;
            _sampleItems = s.ItemsDone;
            return;
        }
        double seconds = (s.Now - start).TotalSeconds;
        long work = s.WorkDone - _sampleWork, items = s.ItemsDone - _sampleItems;
        if (seconds < SampleLength.TotalSeconds || work == 0 && items == 0) return;
        Add(seconds, work / Megabyte, items);
        _sampleStart = s.Now;
        _sampleWork = s.WorkDone;
        _sampleItems = s.ItemsDone;
    }

    private void Add(double seconds, double megabytes, double items)
    {
        _moving += seconds;
        _samples++;
        _sampleSeconds = _samples == 1 ? seconds : _sampleSeconds * 0.8 + seconds * 0.2;
        // The short memory takes every sample as it is, to follow a change of pace; the long memory takes a sample far
        // off its own model (an antivirus scan, a burst into the write cache) only up to a limit, to stay steady.
        _short.Add(seconds, megabytes, items);
        double limited = seconds;
        if (_long.TrySolve(out var m) && m.Predict(megabytes, items) is var expected and > 0)
            limited = Math.Clamp(seconds, expected / 8, expected * 8 + SampleLength.TotalSeconds);
        _long.Add(limited, megabytes, items);
        // A lasting change of pace (large files done, small ones begun): the long memory starts over from the short one.
        if (_short.TrySolve(out var fast) && _long.TrySolve(out var slow))
        {
            double a = fast.Predict(megabytes * 20, items * 20), b = slow.Predict(megabytes * 20, items * 20);
            _phaseShift = Math.Abs(a - b) > 0.5 * Math.Max(Math.Min(a, b), 0.001) ? _phaseShift + 1 : Math.Max(0, _phaseShift - 1);
            if (_phaseShift >= 3)
            {
                _long = _short.Clone(120);
                _phaseShift = 0;
            }
        }
    }

    /// <summary>The likely time left and the pessimistic one (seconds), for the work left.</summary>
    private (double Likely, double Pessimistic) Predict(double megabytesLeft, double itemsLeft)
    {
        _long.TrySolve(out var slow);
        double likely = slow.Predict(megabytesLeft, itemsLeft);
        double pessimistic = likely + Z90 * Math.Sqrt(slow.Variance(megabytesLeft, itemsLeft, likely, _sampleSeconds));
        if (_short.TrySolve(out var fast))
        {
            double quick = fast.Predict(megabytesLeft, itemsLeft);
            pessimistic = Math.Max(pessimistic, quick + Z90 * Math.Sqrt(fast.Variance(megabytesLeft, itemsLeft, quick, _sampleSeconds)));
        }
        return (Math.Max(0, likely), Math.Max(likely, pessimistic));
    }

    /// <summary>What is shown follows the estimates: counting down as time passes, rising slowly and falling quickly.</summary>
    private void Show(TimeSpan now, double likely, double pessimistic)
    {
        if (_shownLikely is not { } shownLikely || _shownPessimistic is not { } shownPessimistic)
        {
            _shownLikely = likely;
            _shownPessimistic = pessimistic;
            _shownAt = now;
            return;
        }
        double dt = Math.Max(0, (now - _shownAt).TotalSeconds);
        _shownAt = now;
        double Follow(double shown, double target)
        {
            double countdown = Math.Max(0, shown - dt);
            double tau = target > countdown
                ? target > 3 * countdown + 5 ? 3 : 8 // a real slowdown is followed within seconds, noise barely
                : target < countdown / 3 ? 0.75 : 2.5;
            return countdown + dt / (tau + dt) * (target - countdown);
        }
        _shownLikely = Follow(shownLikely, likely);
        _shownPessimistic = Math.Max(_shownLikely.Value, Follow(shownPessimistic, pessimistic));
    }

    /// <summary>The share of the job's time spent, by the model once there is one, else by bytes or items.</summary>
    private double RawFraction(ProgressSample s)
    {
        double f;
        if (_long.TrySolve(out var m) && m.Predict(s.WorkTotal / Megabyte, s.ItemsTotal) is var all and > 0)
            f = m.Predict(Math.Min(s.WorkDone, s.WorkTotal) / Megabyte, Math.Min(s.ItemsDone, s.ItemsTotal)) / all;
        else if (s.WorkTotal > 0) f = (double)s.WorkDone / s.WorkTotal;
        else if (s.ItemsTotal > 0) f = (double)s.ItemsDone / s.ItemsTotal;
        else f = 0;
        return Math.Clamp(f, 0, 1);
    }

    /// <summary>A fitted model: seconds per megabyte and per item, and what is needed for its uncertainty.</summary>
    private readonly record struct Model(double PerMegabyte, double PerItem, double ResidualVariance, double I11, double I12, double I22)
    {
        public double Predict(double megabytes, double items) => Math.Max(0, PerMegabyte * megabytes + PerItem * items);

        /// <summary>The variance of a prediction: the model's own uncertainty and the noise of the samples still to come.</summary>
        public double Variance(double megabytes, double items, double predicted, double sampleSeconds) =>
            ResidualVariance * (megabytes * megabytes * I11 + 2 * megabytes * items * I12 + items * items * I22)
            + ResidualVariance * (predicted / Math.Max(0.05, sampleSeconds));
    }

    /// <summary>
    /// Weighted least squares of seconds on (megabytes, items) with exponential forgetting over <c>memory</c> seconds of
    /// the job's time, and a weak prior (10 ms per megabyte, 2 ms per item, each worth one sample) that decides what the
    /// samples cannot — how much one item costs while only one large file moves.
    /// </summary>
    private sealed class Fit(double memory)
    {
        private const double PriorPerMegabyte = 0.010, PriorPerItem = 0.002;
        private double _bb, _bn, _nn, _bt, _nt, _tt, _w, _w2;

        public Fit Clone(double memory) => new(memory) { _bb = _bb, _bn = _bn, _nn = _nn, _bt = _bt, _nt = _nt, _tt = _tt, _w = _w, _w2 = _w2 };

        public void Add(double seconds, double megabytes, double items)
        {
            double decay = Math.Exp(-seconds / memory);
            _bb = _bb * decay + megabytes * megabytes;
            _bn = _bn * decay + megabytes * items;
            _nn = _nn * decay + items * items;
            _bt = _bt * decay + megabytes * seconds;
            _nt = _nt * decay + items * seconds;
            _tt = _tt * decay + seconds * seconds;
            _w = _w * decay + 1;
            _w2 = _w2 * decay * decay + 1;
        }

        public bool TrySolve(out Model model)
        {
            model = default;
            if (_w <= 0) return false;
            // Normal equations with the prior as one pseudo-sample for each rate.
            double s11 = _bb + 1, s12 = _bn, s22 = _nn + 1;
            double r1 = _bt + PriorPerMegabyte, r2 = _nt + PriorPerItem;
            double det = s11 * s22 - s12 * s12;
            if (det <= 1e-12) return false;
            double i11 = s22 / det, i12 = -s12 / det, i22 = s11 / det;
            double a = i11 * r1 + i12 * r2, c = i12 * r1 + i22 * r2;
            // Neither rate is negative: where one would be, the other alone explains the samples.
            if (a < 0)
            {
                a = 0;
                c = r2 / s22;
                i11 = 0;
                i12 = 0;
                i22 = 1 / s22;
            }
            else if (c < 0)
            {
                c = 0;
                a = r1 / s11;
                i11 = 1 / s11;
                i12 = 0;
                i22 = 0;
            }
            double rss = _tt - 2 * (a * _bt + c * _nt) + a * a * _bb + 2 * a * c * _bn + c * c * _nn;
            double samples = _w * _w / Math.Max(1e-9, _w2);
            double variance = Math.Max(0, rss) / Math.Max(1, samples - 2);
            // Never more certain than a twentieth of a typical sample: a few steady samples prove little.
            double typical = Math.Sqrt(_tt / Math.Max(1e-9, _w));
            variance = Math.Max(variance, 0.0025 * typical * typical);
            model = new Model(a, c, variance, i11, i12, i22);
            return true;
        }
    }
}
