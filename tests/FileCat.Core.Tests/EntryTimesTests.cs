using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Release issue I45: an FTP server's LIST states times only to the minute, or for older files the day; such a time
/// stands for all of its minute or day when items are compared, so a file and its copy there are not "different".
/// </summary>
public sealed class EntryTimesTests
{
    private static readonly DateTime Day = new(2021, 3, 4, 0, 0, 0, DateTimeKind.Utc);

    private static EntryData At(DateTime utc, EntryFlags precision = EntryFlags.None) => new("f", EntryKind.File, 1, utc.Ticks) { Flags = precision };

    [Theory]
    // Exact times: the tolerance alone decides, as before.
    [InlineData("05:06:07", "", "05:06:08", "", 0)]
    [InlineData("05:06:07", "", "05:06:10", "", -1)]
    [InlineData("05:06:10", "", "05:06:07", "", 1)]
    // A listing's minute stands for all of it.
    [InlineData("05:06:00", "minute", "05:06:59", "", 0)]
    [InlineData("05:06:00", "minute", "05:08:30", "", -1)]
    [InlineData("05:06:00", "minute", "05:03:00", "", 1)]
    // A listing's day stands for all of it: the old file and its copy are the same, a later one is newer.
    [InlineData("00:00:00", "day", "05:06:07", "", 0)]
    [InlineData("00:00:00", "day", "23:59:59", "", 0)]
    [InlineData("00:00:00", "day", "1.00:00:05", "", -1)]
    [InlineData("00:00:00", "day", "-00:00:05", "", 1)]
    // Two coarse times overlap: the same.
    [InlineData("00:00:00", "day", "13:45:00", "minute", 0)]
    public void A_coarse_time_stands_for_its_whole_minute_or_day(string a, string aPrecision, string b, string bPrecision, int expected)
    {
        static EntryFlags P(string p) => p switch { "minute" => EntryFlags.TimeToMinute, "day" => EntryFlags.TimeToDay, _ => EntryFlags.None };
        var left = At(Day + TimeSpan.Parse(a, System.Globalization.CultureInfo.InvariantCulture), P(aPrecision));
        var right = At(Day + TimeSpan.Parse(b, System.Globalization.CultureInfo.InvariantCulture), P(bPrecision));
        Assert.Equal(expected, EntryTimes.Compare(left, right, TimeSpan.FromSeconds(2)));
        Assert.Equal(-expected, EntryTimes.Compare(right, left, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void An_unknown_time_is_neither_newer_nor_older()
    {
        Assert.Null(EntryTimes.Compare(At(Day), new EntryData("f", EntryKind.File, 1, 0), TimeSpan.Zero));
        Assert.Equal(EntryFlags.TimeToDay, EntryTimes.FlagFor(TimeSpan.FromDays(1)));
        Assert.Equal(EntryFlags.TimeToMinute, EntryTimes.FlagFor(TimeSpan.FromMinutes(1)));
        Assert.Equal(EntryFlags.None, EntryTimes.FlagFor(TimeSpan.Zero));
    }
}
