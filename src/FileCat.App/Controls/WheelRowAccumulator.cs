namespace FileCat.App.Controls;

/// <summary>Keeps precision-wheel fractions until they add up to whole visual rows.</summary>
internal sealed class WheelRowAccumulator
{
    private double _fraction;

    internal int Consume(double wheelDelta)
    {
        if (!double.IsFinite(wheelDelta)) { Reset(); return 0; }
        double total = _fraction + wheelDelta * 3;
        if (!double.IsFinite(total)) { Reset(); return 0; }
        if (total >= int.MaxValue) { Reset(); return int.MaxValue; }
        if (total <= int.MinValue) { Reset(); return int.MinValue; }
        // The tolerance only removes binary floating-point noise around a whole row.
        int rows = (int)Math.Truncate(total + Math.Sign(total) * 1e-9);
        _fraction = total - rows;
        if (Math.Abs(_fraction) < 1e-9) _fraction = 0;
        return rows;
    }

    internal void Reset() => _fraction = 0;
}
