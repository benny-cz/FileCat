namespace FileCat.Core.Archives;

/// <summary>Exact UTC instant with nanosecond precision. Seconds use floor division, including before the epoch.</summary>
public readonly record struct ArchiveTimestamp
{
    public long Seconds { get; }
    public int Nanoseconds { get; }
    public decimal UnixSeconds => Seconds + Nanoseconds / 1_000_000_000m;
    public bool FitsTicks => Nanoseconds % 100 == 0;

    public ArchiveTimestamp(long seconds, int nanoseconds)
    {
        if (seconds is < -62135596800L or > 253402300799L || nanoseconds is < 0 or >= 1_000_000_000)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The archive timestamp is outside the supported UTC nanosecond range.");
        Seconds = seconds; Nanoseconds = nanoseconds;
    }

    public static ArchiveTimestamp FromUnixSeconds(decimal value)
    {
        if (value < -62135596800m || value >= 253402300800m || value * 1_000_000_000m != decimal.Truncate(value * 1_000_000_000m))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The archive timestamp cannot be represented exactly in nanoseconds.");
        long seconds = (long)decimal.Floor(value);
        return new(seconds, (int)((value - seconds) * 1_000_000_000m));
    }

    public static implicit operator ArchiveTimestamp(DateTimeOffset value)
    {
        long ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        long seconds = Math.DivRem(ticks, TimeSpan.TicksPerSecond, out long remainder);
        if (remainder < 0) { seconds--; remainder += TimeSpan.TicksPerSecond; }
        return new(seconds, checked((int)(remainder * 100)));
    }

    /// <summary>Exact conversion; callers cannot accidentally round native nanoseconds.</summary>
    public DateTimeOffset ToDateTimeOffset()
    {
        if (!FitsTicks) throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The exact archive timestamp is finer than DateTimeOffset ticks.");
        return DateTimeOffset.FromUnixTimeSeconds(Seconds).AddTicks(Nanoseconds / 100);
    }
}
